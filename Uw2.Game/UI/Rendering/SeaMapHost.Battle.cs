using Uw2.Game.Engine;
using Uw2.Game.Engine.Sea;
using Uw2.Game.Engine.Town;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Rendering;

/// <summary>해전 들머리와 뒤끝 — 바다 상태를 <see cref="Battle"/> 로 옮기고, 끝나면 되돌려 적는다.</summary>
public sealed partial class SeaMapHost
{
    /// <summary>「공격」 이 고를 수 있는 함대 — 제 함대 둘레 두 칩 안의 바다에 나온 남 함대(<c>0x0043CCF0(2)</c>).</summary>
    public List<NpcFleet> FleetsInReach(int reach = 2)
    {
        if (Fleet == null) return [];
        return [.. Others.Where(f => !f.Docked && f.Slots.Count > 0
                                     && Math.Abs((int)f.X - (int)Fleet.X) <= reach && Math.Abs((int)f.Y - (int)Fleet.Y) <= reach)];
    }

    /// <summary>사람 기록의 싸움 몫 — 능력치 여덟(+0x1C), 항해 · 전투 레벨(+0x24 · +0x25), 전투 경험(+0x28 낱말), 포술(+0x30 비트 4).</summary>
    public Battle.Captain BattleCaptain(int person, IReadOnlyList<Battle.Captain>? aides = null) => new()
    {
        Person = person,
        Stats = [.. Enumerable.Range(0, 8).Select(k => HeroStat(person, Navigators.Stats + k))],
        NavLevel = HeroStat(person, Navigators.NavLevel),
        BattleLevel = HeroStat(person, Navigators.BattleLevel),
        BattleExp = HeroStat(person, 0x28) | HeroStat(person, 0x29) << 8,
        Gunnery = (HeroStat(person, 0x30) & 4) != 0,
        Aides = aides ?? [],
    };

    /// <summary>
    /// 일기토 채비(<c>0x00414E20</c>) — 주인공은 지닌 아이템 가운데 장비한 것: 갈래(&amp; 0xF)가 4 아래면 무기(갈래 · 세기 +0x12),
    /// 6 이면 갑옷(세기를 더한다). 남 제독은 <see cref="Duel.NpcGear"/> 의 표에서 고른다.
    /// </summary>
    public Duel.Fighter DuelFighter(Battle.Ship s)
    {
        var c = s.Captain;
        int style = 0, weapon = 0, armor = 0;
        if (Scenario is { } vm)
        {
            var m = vm.Memory;
            IEnumerable<ItemShop.Item> gear;
            if (s.Player && c.Person == HeroPersonNumber)
                gear = ItemShop.Owned(m).Where(i => ItemShop.IsEquipped(m, i)).Select(i => ItemShop.Get(m, i));
            else
            {
                var (w, a) = Duel.NpcGear(c.BattleLevel, c.NavLevel);
                gear = [ItemShop.Get(m, w), ItemShop.Get(m, a)];
            }
            foreach (var it in gear)
            {
                if (it.Group < 4) { style = it.Group; weapon = it.Power; }
                else if (it.Group == 6) armor += it.Power;
            }
        }
        return new Duel.Fighter(PersonName(c.Person), c.Stat(5), c.BattleLevel, style, weapon, armor);
    }

    /// <summary>싸움 뒤 사람 기록에 돌려 적는다 — 능력치 · 전투 레벨 · 경험.</summary>
    private void StoreCaptain(Battle.Captain c)
    {
        if (Scenario is not { } vm) return;
        var m = vm.Memory;
        for (int k = 0; k < 8; k++) Navigators.Set(m, c.Person, Navigators.Stats + k, c.Stats[k]);
        Navigators.Set(m, c.Person, Navigators.BattleLevel, c.BattleLevel);
        Navigators.Set(m, c.Person, 0x28, c.BattleExp & 0xFF);
        Navigators.Set(m, c.Person, 0x29, (c.BattleExp >> 8) & 0xFF);
    }

    /// <summary>
    /// 판 한 판을 차린다(<c>0x00409B70</c>) — 덮치는 쪽이 편 0, 맞는 쪽이 편 1. <paramref name="heroAttacks"/> 면 주인공이 편 0 이다.
    /// 판은 맞는 쪽 함대 둘레 6 x 6 칩이다(<c>0x0040FCA0</c>).
    /// </summary>
    public Battle OpenBattle(NpcFleet enemy, bool heroAttacks = true)
    {
        var start = StartTables ?? throw new InvalidOperationException("SDATA.DAT 가 없습니다");
        var b = new Battle(Roll) { Time = Fleet?.TimeOfDay ?? Fleet.MorningTick };
        HeroSide = heroAttacks ? 0 : 1;
        int hx = (int)(Fleet?.X ?? enemy.X), hy = (int)(Fleet?.Y ?? enemy.Y);
        int ex = (int)enemy.X, ey = (int)enemy.Y;
        int dx = heroAttacks ? ex : hx, dy = heroAttacks ? ey : hy;   // 맞는 쪽 자리
        b.BuildMap((cx, cy) => Fleet?.IsSea(((dx - 2 + cx) % ChipMap.Width + ChipMap.Width) % ChipMap.Width, dy - 2 + cy) ?? true);
        b.RollWind();

        // 제 함대 — 기함이 앞. 주인공 배에는 부선장 · 수석항해사가 거든다.
        int hero = HeroPersonNumber;
        var aides = Scenario is { } vm
            ? (int[])[Navigators.InPost(vm.Memory, 0), Navigators.InPost(vm.Memory, 2)]
            : [];
        var aideCaps = aides.Where(p => p >= 0).Select(p => BattleCaptain(p)).ToList();
        foreach (var ship in Player.Ships)
        {
            int who = ship.Captain >= 0 ? ship.Captain : hero;
            var cap = BattleCaptain(who, who == hero ? aideCaps : null);
            var type = ship.Type < start.ShipTypes.Count ? start.ShipTypes[ship.Type] : default;
            bool sailOnly = ship.Type < start.Templates.Count && !start.Templates[ship.Type].Oars;
            b.Add(HeroSide, cap, ship.Name, ship.Crew, ship.Hull, ship.MaxHull, ship.Upwind, ship.Sail, ship.GunKind, ship.Mounted,
                  ship.Supplies[3], 100 - ship.SailShare - ship.WatchShare, player: true, sailOnly, merchant: false,
                  type.Durability, ship.MaxCrew, ship);
        }

        AddNpc(b, EnemySide, enemy);

        // 원군(0x00409C30) — 둘레 두 칩 안의 나라 해군(번호 % 10 이 5 이상) 가운데 맞는 쪽 나라면 편 3(말 0x30B), 덮치는 쪽 나라면 편 2(0x30C).
        // 편마다 하나, 주인공이 해적이면 없다.
        BattleAllies.Clear();
        int attackerNation = heroAttacks ? HeroNation : enemy.Nation, defenderNation = heroAttacks ? enemy.Nation : HeroNation;
        if (HeroNation != Economy.NoNation)
            foreach (var f in FleetsInReach())
            {
                if (f == enemy || f.Pirate || f.Index % 10 < 5) continue;
                int side = f.Nation == defenderNation && !BattleAllies.ContainsKey(3) ? 3
                         : f.Nation == attackerNation && !BattleAllies.ContainsKey(2) ? 2 : -1;
                if (side < 0) continue;
                BattleAllies[side] = f;
                AddNpc(b, side, f);
            }

        // 펼치기 — 함대마다 맞는 쪽과의 칩 차(0x00410960).
        b.Deploy(HeroSide, Fleet?.Heading ?? 0, hx - dx, hy - dy);
        b.Deploy(EnemySide, enemy.Heading, ex - dx, ey - dy);
        foreach (var (side, f) in BattleAllies) b.Deploy(side, f.Heading, (int)f.X - dx, (int)f.Y - dy);
        return b;
    }

    /// <summary>이번 판에서 주인공 편(0 덮침 · 1 맞음)과 맞서는 편.</summary>
    public int HeroSide { get; private set; }
    public int EnemySide => HeroSide ^ 1;

    /// <summary>이번 판에 든 원군 — 편 2 · 3.</summary>
    public Dictionary<int, NpcFleet> BattleAllies { get; } = [];

    /// <summary>
    /// 남 함대를 편에 들인다 — 배마다 제독이 선장이다(<c>0x00410423</c>). 탄약은 나라 해군(번호 % 10 이 0 · 5 ~ 9)의 돛배만
    /// 선종 대포 위끝 / 5, 그리고 rand 를 더한다(<c>0x00410801</c>).
    /// </summary>
    private void AddNpc(Battle b, int side, NpcFleet fleet)
    {
        var start = StartTables!;
        var admiral = BattleCaptain(fleet.Admiral);
        int kind = fleet.Index % 10;
        bool navy = kind == 0 || kind >= 5;
        bool merchant = !fleet.Pirate && kind is >= 1 and <= 4;
        foreach (var slot in fleet.Slots)
        {
            int typeIndex = start.GuessType(slot);
            var type = start.ShipTypes[typeIndex];
            bool sailOnly = typeIndex < start.Templates.Count && !start.Templates[typeIndex].Oars;
            int k5 = type.MaxCrew / 5;
            int shot = (navy && sailOnly ? k5 : 0) + Roll(k5);
            string name = typeIndex < start.Templates.Count ? start.Templates[typeIndex].Name : $"#{typeIndex}";
            b.Add(side, admiral, name, slot.Crew, slot.Durability, slot.MaxDurability, slot.Upwind, slot.Sail, slot.Flags & 7,
                  slot.Field6, shot, sailOnly ? 25 : 50, player: false, sailOnly, merchant, type.Durability, slot.Crew, slot);
        }
    }

    /// <summary>남 함대 배 칸에 판의 결과를 돌려 적는다. 다 잃으면 바다에서 뺀다.</summary>
    private void StoreNpc(Battle b, int side, NpcFleet fleet, bool flagshipLost)
    {
        fleet.Slots.Clear();
        foreach (var s in b.SideShips(side))
            if (s.Tag is StartData.Slot slot && s.Crew > 0 && s.Hull > 0)
                fleet.Slots.Add(slot with { Crew = s.Crew, Durability = s.Hull, MaxDurability = s.MaxHull, Upwind = s.Upwind, Sail = s.Sail });
        if (flagshipLost || fleet.Slots.Count == 0) Others.Remove(fleet);
    }

    /// <summary>
    /// 뒤끝(<c>0x0040AA10</c>) — 제 배의 선원 · 내구 · 돛 · 역풍 · 탄약을 돌려 적고, 가라앉거나 선원을 다 잃은 배는 잃는다.
    /// 이기면(2 · 4 · 6) 명성 200 + min(적 제독 전투 레벨, 30) x min(그것, 15) x 덤을 얻는다(<c>0x0040B025</c>, 50000 까지).
    /// 적 기함이 쓰러지면 그 함대는 바다에서 빠진다. 돌려주는 것은 얻은 명성.
    /// </summary>
    public Spoils CloseBattle(Battle b, NpcFleet enemy)
    {
        foreach (var s in b.SideShips(HeroSide))
        {
            if (s.Tag is not PlayerState.Ship ship) continue;
            ship.Crew = s.Crew; ship.Hull = s.Hull; ship.MaxHull = s.MaxHull;
            ship.Upwind = s.Upwind; ship.Sail = s.Sail; ship.Supplies[3] = s.Shot;
        }
        foreach (var c in b.SideShips(HeroSide).Select(s => s.Captain).Distinct()) StoreCaptain(c);
        var lost = b.SideShips(HeroSide).Where(s => s.Crew == 0 || s.Hull == 0).Select(s => s.Tag).OfType<PlayerState.Ship>().ToList();
        if (lost.Count < Player.Ships.Count)
            foreach (var ship in lost)
            {
                int i = Player.Ships.IndexOf(ship);
                Player.Ships.RemoveAt(i);
                if (Fleet != null && i < Fleet.Ships.Count) Fleet.Ships.RemoveAt(i);
            }

        // 남 함대의 배 칸 — 나포할 배를 고르기 전에 적어 두고, 원군도 같이 적는다.
        foreach (var (side, f) in BattleAllies) StoreNpc(b, side, f, false);

        var prizes = new List<Battle.Ship>();
        int gold = 0;
        string? item = null;
        if (b.Result is Battle.Outcome.EnemySunk or Battle.Outcome.DuelWon)
            (gold, item, prizes) = Plunder(b, enemy);
        StoreNpc(b, EnemySide, enemy, b.Result is Battle.Outcome.EnemySunk or Battle.Outcome.DuelWon);
        int fame = 0;
        if (b.Result is (Battle.Outcome.EnemyLeft or Battle.Outcome.EnemySunk or Battle.Outcome.DuelWon) && Scenario is { } vm)
        {
            // 덤: 적 기함 이탈 1, 그 밖의 승리 3(0x0040ACF0), 해적이면 x 3. 나라 사이 덤(0x0040B0E0)은 아직 안 옮겼다.
            int mult = b.Result == Battle.Outcome.EnemyLeft ? 1 : 3;
            if (enemy.Pirate) mult *= 3;
            int lv = HeroStat(enemy.Admiral, Navigators.BattleLevel);
            fame = Sighting.AddFame(vm.Memory, HeroPersonNumber, 200 + Math.Min(lv, 30) * Math.Min(lv, 15) * mult);
        }
        SyncLoad();
        if (b.Result is Battle.Outcome.PlayerSunk or Battle.Outcome.DuelLost)
            Dispatcher.BeginInvoke(() => GameOver?.Invoke(EndBattle));
        return new Spoils(fame, gold, item, prizes);
    }

    /// <summary>싸움 뒤 얻은 것 — 명성 · 금화 · 아이템 이름 · 나포할 수 있는 배.</summary>
    public sealed record Spoils(int Fame, int Gold, string? Item, List<Battle.Ship> Prizes);

    /// <summary>
    /// 노획(<c>0x0040B1F0</c>) — 금화 (함대 +0x24 낱말 + 1) x 100(말 0x230), 아이템 함대 +0x1D(0x231, 지닌 것이 스물 아래일 때).
    /// 나포할 배(<c>0x0040B320</c>)는 내구가 남은 적 배 가운데 <b>화면 칸(13 x 12) 안</b>에 있는 것(<c>0x0040DCD0</c>) — 화면은 제 기함을 따른다.
    /// </summary>
    private (int Gold, string? Item, List<Battle.Ship> Prizes) Plunder(Battle b, NpcFleet enemy)
    {
        int gold = (enemy.Gold + 1) * 100;
        Player.SetGold(Player.Gold + gold);
        string? item = null;
        int it = enemy.Items[0];
        if (it != 0xFF && Scenario is { } m && ItemShop.Owned(m.Memory).Count < ItemShop.OwnedSlots && ItemShop.Give(m.Memory, it))
            item = ItemShop.Get(m.Memory, it).Name;

        var f = b.PlayerFlagship;
        int vx = Math.Clamp((f?.X ?? 0) - 6, 0, Battle.Width - 13), vy = Math.Clamp((f?.Y ?? 0) - 6, 0, Battle.Height - 12);
        var prizes = b.SideShips(EnemySide).Where(s => s.Hull > 0 && s.X >= vx && s.X < vx + 13 && s.Y >= vy && s.Y < vy + 12).ToList();
        return (gold, item, prizes);
    }

    /// <summary>
    /// 나포한 배를 맡을 사람(<c>0x0043CA90</c>) — 주인공이 배를 안 맡고 있으면 주인공(-1), 그리고 처지가 2 보다 큰 동료(쉬는 이 · 자리에 앉은 이).
    /// </summary>
    public List<int> PrizeCaptains()
    {
        var list = new List<int>();
        if (Player.Ships.All(s => s.Captain != -1)) list.Add(-1);
        if (Scenario is { } vm)
            list.AddRange(Navigators.Mates(vm.Memory).Select(t => t.Person).Where(p => Navigators.Get(vm.Memory, p, Navigators.Status) > Navigators.Captain));
        return list;
    }

    /// <summary>
    /// 나포(<c>0x0040B810</c>) — 배 칸을 베끼고 선원은 min(그 배 선원 / 10, 선원 자리), 보급 · 짐은 비우고 승무원비율 50 · 25, 사기 60.
    /// 맡은 동료는 처지 2(선장)가 된다. 이름은 화면이 받는다(말 0x22D, 일곱 자).
    /// </summary>
    public bool TakePrize(Battle.Ship prize, int captain, string name)
    {
        if (StartTables is not { } start || prize.Tag is not StartData.Slot slot) return false;
        int type = start.GuessType(slot);
        if (!AddShip(type, captain: captain)) return false;
        var ship = Player.Ships[^1];
        ship.Name = name;
        ship.Crew = Math.Min(prize.Crew / 10, ship.MaxCrew);
        ship.Hull = prize.Hull; ship.MaxHull = prize.MaxHull; ship.Upwind = prize.Upwind; ship.Sail = prize.Sail;
        ship.GunKind = prize.GunKind; ship.Mounted = Math.Min(prize.Guns, Math.Max(ship.Guns, prize.Guns));
        if (Fleet != null && Fleet.Ships.Count == Player.Ships.Count) Fleet.Ships[^1] = Fleet.Ships[^1] with { Crew = ship.Crew };
        SyncLoad();
        return true;
    }
}
