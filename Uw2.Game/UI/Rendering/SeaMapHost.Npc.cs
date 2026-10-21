using Uw2.Game.Engine;
using Uw2.Game.Engine.Sea;
using Uw2.Game.Engine.Town;

namespace Uw2.Game.UI.Rendering;

/// <summary>남 함대가 보는 바깥(<see cref="NpcFleet.IWorld"/>) — 항구 · 나라 표 · 주인공 함대.</summary>
public sealed partial class SeaMapHost : NpcFleet.IWorld
{
    (int X, int Y)? NpcFleet.IWorld.PortPos(int port) =>
        Ports is { } pt && port >= 0 && port < pt.Ports.Count ? (pt.Ports[port].X, pt.Ports[port].Y) : null;

    int NpcFleet.IWorld.Nation(int nation, int field) =>
        Scenario?.Memory.Read(ScenarioMemory.At(ScenarioMemory.NationTable, nation, field), false) ?? 0;

    int NpcFleet.IWorld.NationPower(int nation) =>
        Scenario?.Memory.Read(ScenarioMemory.At(ScenarioMemory.NationTable, nation, 0), true) ?? 0;

    Support.Local.Formats.Economy? NpcFleet.IWorld.Economy => Economy;

    int NpcFleet.IWorld.HeroRank => HeroRank;
    int NpcFleet.IWorld.HeroNation => HeroNation;
    int NpcFleet.IWorld.HeroFleetIndex =>
        Scenario is { } vm ? Navigators.Get(vm.Memory, HeroPersonNumber, Navigators.Fleet) : HeroPersonNumber * 10;
    int NpcFleet.IWorld.HeroAdmiral => HeroPersonNumber;
    bool NpcFleet.IWorld.HeroAtSea => AtSea && CurrentPort < 0 && Fleet != null;
    (int X, int Y) NpcFleet.IWorld.HeroCell => Fleet is { } f ? ((int)f.X, (int)f.Y) : (-1, -1);
    NpcFleet? NpcFleet.IWorld.FleetByIndex(int index) => Others.FirstOrDefault(o => o.Index == index);

    /// <summary>
    /// 달마다 나라 셈(<c>0x0042B960</c>, <see cref="Nations.NewMonth"/>) — 항구 백의 임자 · 경제 · 공업 · 바다 갈래를 모아 넘긴다.
    /// </summary>
    private void NationsMonth()
    {
        if (Scenario is not { } vm || Economy?.StateBytes is not { } raw) return;
        var m = vm.Memory;
        var ports = new List<(int, bool, int, int, int)>();
        for (int p = 0; p < Sighting.PortCount && (p + 1) * Support.Local.Formats.Economy.StateSize <= raw.Length; p++)
        {
            int flags = m.Read(ScenarioMemory.At(ScenarioMemory.PortTable, p, 0x15), false);
            int o = p * Support.Local.Formats.Economy.StateSize;
            ports.Add((flags & 7, (flags & 8) != 0, raw[o] | raw[o + 1] << 8, raw[o + 4] | raw[o + 5] << 8, raw[o + 0x1C]));
        }
        int hero = HeroPersonNumber;
        Nations.NewMonth(m, ports, n => m.Read(ScenarioMemory.At(ScenarioMemory.HeroTable, hero, 6 + n), false), HeroNation, Roll,
                         p => p < ports.Count ? ports[p].Item1 : Nations.Pirates);
        Nations.Relations(m, Roll, p => p < ports.Count ? ports[p].Item1 : Nations.Pirates, (p, by) =>
        {
            // 빼앗은 항구(0x0042C0D0) — 임자를 바꾸고 그 나라 몫을 rand(10) + 80 으로.
            var at = ScenarioMemory.At(ScenarioMemory.PortTable, p, 0x15);
            m.Write(at, (m.Read(at, false) & 0xF8) | by, false);
            int o = p * Support.Local.Formats.Economy.StateSize;
            if (o + 8 + by < raw.Length) raw[o + 8 + by] = (byte)(Roll(10) + 80);
            Economy?.RestoreStates(raw);
        });
        Nations.Aid(m, hero);
        ForgeFleets();
        RivalFleets(m, hero);
    }

    /// <summary>
    /// 맞수 함대 키우기(<c>0x0042C330</c>) — 다른 주인공(사람 0 ~ 5)과, +0x2F &amp; 4 · +0x31 &amp; 0x20 · 처지 1 이고 해적이거나 해군(번호 % 10 ≥ 5)
    /// 함대를 맡은 사람마다 그 함대에 배 한 척을 들이거나 바꾼다. 제 함대 세기(<c>0x0042ACA0</c>)가 그 함대보다 크지 않으면 셋에 하나는 건너뛴다.
    /// 빈 칸이 있으면 그 칸, 없으면 배 기록 번호가 가장 작은 칸에 기록 (표 <c>0x00458F1C</c>[갈래] &gt;&gt; (작위 / 5 x 4)) &amp; 0xF + 0x31 —
    /// 갈래는 사람 0 · 3 · 4 → 0, 1 → 1, 2 → 2, 5 → 3, 그 밖은 해군 7 ~ 9 이면 2 아니면 1. 바이트 표라 작위 10 넘으면 늘 0x31 이다.
    /// </summary>
    private void RivalFleets(ScenarioMemory m, int hero)
    {
        if (StartTables is not { } start) return;
        ReadOnlySpan<byte> table = [0x05, 0xBC, 0x15, 0x02];
        int k = m.Read(ScenarioMemory.At(ScenarioMemory.HeroTable, hero, ScenarioMemory.RankField), false) / 5;
        int ours = (Player.Ships.Sum(s => s.Hull) + 4) / 5;
        for (int p = 0; p < Navigators.People; p++)
        {
            if (p == hero) continue;
            int fleet = Navigators.Get(m, p, Navigators.Fleet);
            if (p >= 6)
            {
                if ((Navigators.Get(m, p, Navigators.Mood) & 4) == 0 || (Navigators.Get(m, p, Navigators.Kind) & 0x20) == 0
                    || Navigators.Get(m, p, Navigators.Status) != 1) continue;
                if (fleet < 60 && fleet % 10 < 5) continue;
            }
            if (fleet >= Support.Local.Formats.StartData.FleetCount) continue;
            var slots = Others.FirstOrDefault(o => o.Index == fleet)?.Slots
                        ?? (FleetReserve.TryGetValue(fleet, out var r) ? r : FleetReserve[fleet] = []);
            int theirs = (slots.Sum(s => s.Durability) + 4) / 5;
            if (ours <= theirs && Roll(3) == 0) continue;
            int band = p < 6 ? (p switch { 1 => 1, 2 => 2, 5 => 3, _ => 0 }) : fleet % 10 >= 7 ? 2 : 1;
            int shift = k * 4;
            int record = (shift < 8 ? table[band] >> shift & 0xF : 0) + 0x31;
            var slot = FleetForge.Slot(start, record, 1, Roll);
            if (slots.Count < Support.Local.Formats.StartData.SlotCount) slots.Add(slot);
            else
            {
                int weakest = 0;
                for (int i = 1; i < slots.Count; i++) if (slots[i].Ship < slots[weakest].Ship) weakest = i;
                slots[weakest] = slot;
            }
        }
    }

    /// <summary>바다에 없는 함대의 배 칸(함대 번호 → 칸) — 달마다 채우고(<see cref="FleetForge"/>), 넉넉해지면 내보낸다.</summary>
    public Dictionary<int, List<Support.Local.Formats.StartData.Slot>> FleetReserve { get; } = [];

    /// <summary>
    /// 함대 다시 짓기(<c>0x0042B0F0</c>) — 빈 칸 채우기 뒤, 번호 % 10 이 0 이 아니고 바다에 없는 함대가 rand(2) + 4 척 넘으면
    /// 제독을 세워(<c>0x0042B3E0</c>) 본거지에서 내보낸다(<c>0x0042B030</c>).
    /// </summary>
    private void ForgeFleets()
    {
        if (Scenario is not { } vm || StartTables is not { } start || Fleet == null || Routes is not { } routes || Ports is not { } pts) return;
        var m = vm.Memory;
        int Power(int n) => (short)m.Read(ScenarioMemory.At(ScenarioMemory.NationTable, n, 0), true);
        bool Idle(int index) => index % 10 != 0 && Others.All(o => o.Index != index);
        List<Support.Local.Formats.StartData.Slot> Reserve(int index) =>
            FleetReserve.TryGetValue(index, out var list) ? list : FleetReserve[index] = [];
        FleetForge.Fill(start, Power, Idle, Reserve, Roll);

        for (int index = 0; index < Support.Local.Formats.StartData.FleetCount; index++)
        {
            if (!Idle(index) || !FleetForge.Ready(Reserve(index).Count, Roll)) continue;
            int nation = Math.Min(Nations.Pirates, index / 10);
            int admiral = PickAdmiral(m, index, nation);
            if (admiral < 0) continue;
            int capital = m.Read(ScenarioMemory.At(ScenarioMemory.NationTable, nation, 0x0A), false);
            if (capital >= pts.Ports.Count) continue;
            var at = (pts.Ports[capital].X, pts.Ports[capital].Y);
            var slots = Reserve(index).Take(Support.Local.Formats.StartData.SlotCount).ToList();
            var record = new Support.Local.Formats.StartData.Fleet(index, at.X, at.Y, 0, admiral, capital, 0x31, at.X, at.Y, capital, slots);
            var npc = new NpcFleet(Fleet, routes, _npcRng, record, start, at, at) { World = this, Mode = 0, Goal = capital };
            npc.Gold = Power(nation) / 10 + Roll(3) + 1;
            for (int i = 0; i < npc.Items.Length; i++) npc.Items[i] = 0xFF;
            npc.CargoGood = -1; npc.CargoAmount = 0;
            npc.StayInPort(Roll(10) + 5);
            FleetReserve.Remove(index);
            Others.Add(npc);
            _othersScale = -1;
        }
    }

    /// <summary>
    /// 제독 고르기(<c>0x0042B3E0</c>) — 둘에 하나는 쉬는 사람(6 ~ 119)에서: 나라 함대는 그 나라(+0x31 &amp; 0xF)이고 0x10 이 없고 0x20 이 있으며
    /// 함대(+0x2C)가 없는 사람, 해적은 함대가 없고 +0x2F &amp; 3 이 2 이상이고 0x20 이 있는 사람. 나머지 하나는 0x20 이 없는 첫 칸에
    /// 새 사람을 지어낸다(<see cref="Navigators.Forge"/>). 고르면 +0x2C = 함대, +0x2D = 0xFF, +0x2E = 1.
    /// </summary>
    private int PickAdmiral(ScenarioMemory m, int index, int nation)
    {
        if (Roll(2) != 0)
        {
            // 새 사람 — 사람 표에서 0x20 이 없는 첫 칸(6 ~ 119)을 지어낸다.
            for (int p = 6; p < Navigators.People; p++)
                if ((Navigators.Get(m, p, Navigators.Kind) & 0x20) == 0)
                {
                    if (NameTable == null) return -1;
                    Navigators.Forge(m, p, nation, Roll(2), NameTable, Roll);
                    return Appoint(p);
                }
            return -1;
        }
        for (int p = 6; p < Navigators.People; p++)
        {
            int kind = Navigators.Get(m, p, Navigators.Kind);
            if (Navigators.Get(m, p, Navigators.Fleet) != 0xFF || (kind & 0x20) == 0) continue;
            bool fits = index < 60 ? (kind & 0x0F) == nation && (kind & 0x10) == 0 : (Navigators.Get(m, p, Navigators.Mood) & 3) >= 2;
            if (!fits) continue;
            return Appoint(p);
        }
        return -1;

        int Appoint(int p)
        {
            Navigators.Set(m, p, Navigators.Fleet, index);
            Navigators.Set(m, p, Navigators.Port, 0xFF);
            Navigators.Set(m, p, Navigators.Status, 1);
            return p;
        }
    }

    /// <summary>새 사람 이름 표(<c>NAME.TBL</c>, 26 바이트 x 96 — 나라마다 열여섯, 이름 13 · 성 13).</summary>
    public byte[]? NameTable { get; private set; }

    /// <summary>
    /// 함대가 쓰러졌다(<c>0x0040A9A0</c>) — 제독은 함대를 잃고(+0x2C = 0xFF, +0x2E = 0, +0x2D = rand(42)) 배 칸은 다 빈다.
    /// </summary>
    private void FleetLost(NpcFleet fleet)
    {
        FleetReserve[fleet.Index] = [];
        if (Scenario is not { } vm) return;
        var m = vm.Memory;
        // 제독은 함대를 잃는다 — +0x1B 에 0xC0 이 있거나 열에 하나는 사람 칸에서 빠진다(0x004189DC).
        Navigators.Set(m, fleet.Admiral, Navigators.Status, 0);
        Navigators.Set(m, fleet.Admiral, Navigators.Fleet, 0xFF);
        if ((Navigators.Get(m, fleet.Admiral, 0x1B) & 0xC0) != 0 || Roll(10) == 0)
        {
            Navigators.Set(m, fleet.Admiral, Navigators.Port, 0xFF);
            Navigators.Set(m, fleet.Admiral, Navigators.Kind, Navigators.Get(m, fleet.Admiral, Navigators.Kind) & ~0x20);
        }
        else Navigators.Set(m, fleet.Admiral, Navigators.Port, Roll(42));
    }

    /// <summary>남 함대가 제 함대를 덮쳤다(<c>0x00418622</c>) — 창이 만남 목록을 띄우고 <see cref="AmbushDone"/> 를 부른다.</summary>
    public event Action<NpcFleet>? Ambushed;

    private NpcFleet? _ambush;

    /// <summary>덮친 함대를 다 치렀다.</summary>
    public void AmbushDone(NpcFleet fleet)
    {
        fleet.AfterBattle();
        _ambush = null;
    }

    /// <summary>틱마다 — 맞닿은 함대가 있으면 하나만 알린다. 알린 동안은 바다가 멈춘다. 남끼리 맞닿으면 그 자리에서 셈한다.</summary>
    private bool CheckAmbush()
    {
        if (_ambush != null || Fleet == null) return _ambush != null;
        foreach (var npc in Others)
            if (npc.MeetsHero(Fleet.TimeOfDay))
            {
                _ambush = npc;
                Dispatcher.BeginInvoke(() => Ambushed?.Invoke(npc));
                return true;
            }
        if (Fleet.TimeOfDay is >= 18 and < 54)
            foreach (var npc in Others.ToList())
            {
                if (!Others.Contains(npc)) continue;
                var target = Others.FirstOrDefault(npc.MeetsFleet);
                if (target != null) Clash(npc, target);
            }
        return false;
    }

    /// <summary>
    /// 남 함대끼리의 싸움(<c>0x0041868A</c> ~ <c>0x00418989</c>). 과녁 제독 항해술이 rand(150) 보다 크면 빠져나가고 덮친 쪽은 고향으로 간다.
    /// 아니면 덮친 쪽도 고향으로 돌아가며(갈래 7 은 남는다) 서로 세기(<c>0x0042ACA0</c>) / 10 만큼을 뒤 배부터 내구에서 깎는다 —
    /// 내구가 다 깎인 배는 가라앉고, 과녁 배가 가라앉을 때마다 그 짐의 반을 1000 까지, 돈의 반을 60000 까지 덮친 쪽이 가져간다.
    /// 배를 다 잃은 함대는 쓰러진다(<c>0x00418990</c>). 끝으로 덮친 나라의 과녁 나라 사이가 rand(2) + 2 줄고, 과녁 나라는 그것을 따라 적는다
    /// (덮친 쪽이 해적이면 과녁 나라의 해적 사이가 준다).
    /// </summary>
    private void Clash(NpcFleet attacker, NpcFleet target)
    {
        if (Scenario is not { } vm) return;
        var m = vm.Memory;
        int Power(NpcFleet f) => (f.Slots.Sum(s => s.Durability) + 4) / 5;
        if (Navigators.Get(m, target.Admiral, Navigators.Stats + 1) > Roll(150))
        {
            if (attacker.Mode != 7) attacker.GoHome();
            return;
        }
        if (attacker.Mode != 7) attacker.GoHome();
        int hitTarget = Power(attacker) / 10, hitAttacker = Power(target) / 10;
        Hurt(target, hitTarget, sunk =>
        {
            int half = target.CargoAmount / 2;
            target.CargoAmount -= half;
            attacker.CargoAmount += Math.Min(1000 - attacker.CargoAmount, half);
            attacker.CargoGood = target.CargoGood;
            int g = target.Gold / 2;
            target.Gold -= g;
            attacker.Gold += Math.Min(60000 - attacker.Gold, g);
        });
        if (target.Slots.Count == 0) { Others.Remove(target); FleetLost(target); attacker.GoHome(); }
        Hurt(attacker, hitAttacker, _ => { });
        if (attacker.Slots.Count == 0) { Others.Remove(attacker); FleetLost(attacker); }

        int an = attacker.Index / 10, tn = target.Index / 10;
        ScenarioMemory.Pointer Rel(int a, int b) => ScenarioMemory.At(ScenarioMemory.NationTable, a, Nations.RelationField + b);
        if (an < 6)
        {
            int rel = m.Read(Rel(an, tn), false);
            m.Write(Rel(an, tn), rel - Math.Min(rel, Roll(2) + 2), false);
        }
        if (tn < 6)
        {
            if (an < 6) m.Write(Rel(tn, an), m.Read(Rel(an, tn), false), false);
            else
            {
                int rel = m.Read(Rel(tn, an), false);
                m.Write(Rel(tn, an), rel - Math.Min(rel, Roll(2) + 2), false);
            }
        }

        static void Hurt(NpcFleet f, int points, Action<int> sunk)
        {
            for (int i = f.Slots.Count - 1; i >= 0 && points > 0; i--)
            {
                var s = f.Slots[i];
                if (points >= s.Durability)
                {
                    points -= s.Durability;
                    f.Slots.RemoveAt(i);
                    sunk(i);
                }
                else
                {
                    f.Slots[i] = s with { Durability = s.Durability - points };
                    points = 0;
                }
            }
        }
    }
}
