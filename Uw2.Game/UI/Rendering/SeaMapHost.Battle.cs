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
    /// 판 한 판을 차린다(<c>0x00409B70</c>) — 주인공이 덮치는 쪽(편 0), 그 함대가 맞는 쪽(편 1). 원군은 아직 없다.
    /// 판은 맞는 쪽 함대 둘레 6 x 6 칩이다(<c>0x0040FCA0</c>).
    /// </summary>
    public Battle OpenBattle(NpcFleet enemy)
    {
        var start = StartTables ?? throw new InvalidOperationException("SDATA.DAT 가 없습니다");
        var b = new Battle(Roll) { Time = Fleet?.TimeOfDay ?? Fleet.MorningTick };
        int ex = (int)enemy.X, ey = (int)enemy.Y;
        b.BuildMap((cx, cy) => Fleet?.IsSea(((ex - 2 + cx) % ChipMap.Width + ChipMap.Width) % ChipMap.Width, ey - 2 + cy) ?? true);
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
            b.Add(0, cap, ship.Name, ship.Crew, ship.Hull, ship.MaxHull, ship.Upwind, ship.Sail, ship.GunKind, ship.Mounted,
                  ship.Supplies[3], 100 - ship.SailShare - ship.WatchShare, player: true, sailOnly, merchant: false,
                  type.Durability, ship.MaxCrew, ship);
        }

        // 남 함대 — 배마다 제독이 선장이다(0x00410423). 탄약은 나라 해군(번호 % 10 이 0 · 5 ~ 9)의 돛배만 선종 대포 위끝 / 5, 그리고 rand 를 더한다.
        var admiral = BattleCaptain(enemy.Admiral);
        int kind = enemy.Index % 10;
        bool navy = kind == 0 || kind >= 5;
        bool merchant = !enemy.Pirate && kind is >= 1 and <= 4;
        foreach (var slot in enemy.Slots)
        {
            int typeIndex = start.GuessType(slot);
            var type = start.ShipTypes[typeIndex];
            bool sailOnly = typeIndex < start.Templates.Count && !start.Templates[typeIndex].Oars;
            int k5 = type.MaxCrew / 5;
            int shot = (navy && sailOnly ? k5 : 0) + Roll(k5);
            string name = typeIndex < start.Templates.Count ? start.Templates[typeIndex].Name : $"#{typeIndex}";
            b.Add(1, admiral, name, slot.Crew, slot.Durability, slot.MaxDurability, slot.Upwind, slot.Sail, slot.Flags & 7,
                  slot.Field6, shot, sailOnly ? 25 : 50, player: false, sailOnly, merchant, type.Durability, slot.Crew, slot);
        }

        // 펼치기 — 함대마다 맞는 쪽과의 칩 차(0x00410960).
        int fx = (int)(Fleet?.X ?? ex), fy = (int)(Fleet?.Y ?? ey);
        b.Deploy(0, Fleet?.Heading ?? 0, fx - ex, fy - ey);
        b.Deploy(1, enemy.Heading, 0, 0);
        return b;
    }

    /// <summary>
    /// 뒤끝(<c>0x0040AA10</c>) — 제 배의 선원 · 내구 · 돛 · 역풍 · 탄약을 돌려 적고, 가라앉거나 선원을 다 잃은 배는 잃는다.
    /// 이기면(2 · 4 · 6) 명성 200 + min(적 제독 전투 레벨, 30) x min(그것, 15) x 덤을 얻는다(<c>0x0040B025</c>, 50000 까지).
    /// 적 기함이 쓰러지면 그 함대는 바다에서 빠진다. 돌려주는 것은 얻은 명성.
    /// </summary>
    public int CloseBattle(Battle b, NpcFleet enemy)
    {
        foreach (var s in b.SideShips(0))
        {
            if (s.Tag is not PlayerState.Ship ship) continue;
            ship.Crew = s.Crew; ship.Hull = s.Hull; ship.MaxHull = s.MaxHull;
            ship.Upwind = s.Upwind; ship.Sail = s.Sail; ship.Supplies[3] = s.Shot;
        }
        foreach (var c in b.SideShips(0).Select(s => s.Captain).Distinct()) StoreCaptain(c);
        var lost = b.SideShips(0).Where(s => s.Crew == 0 || s.Hull == 0).Select(s => s.Tag).OfType<PlayerState.Ship>().ToList();
        if (lost.Count < Player.Ships.Count)
            foreach (var ship in lost)
            {
                int i = Player.Ships.IndexOf(ship);
                Player.Ships.RemoveAt(i);
                if (Fleet != null && i < Fleet.Ships.Count) Fleet.Ships.RemoveAt(i);
            }

        // 남 함대의 배 칸.
        enemy.Slots.Clear();
        foreach (var s in b.SideShips(1))
            if (s.Tag is StartData.Slot slot && s.Crew > 0 && s.Hull > 0)
                enemy.Slots.Add(slot with { Crew = s.Crew, Durability = s.Hull, MaxDurability = s.MaxHull, Upwind = s.Upwind, Sail = s.Sail });
        if (b.Result is Battle.Outcome.EnemySunk or Battle.Outcome.DuelWon || enemy.Slots.Count == 0) Others.Remove(enemy);

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
        return fame;
    }
}
