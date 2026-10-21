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
        Navigators.Set(m, fleet.Admiral, Navigators.Status, 0);
        Navigators.Set(m, fleet.Admiral, Navigators.Fleet, 0xFF);
        Navigators.Set(m, fleet.Admiral, Navigators.Port, Roll(42));
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

    /// <summary>틱마다 — 맞닿은 함대가 있으면 하나만 알린다. 알린 동안은 바다가 멈춘다.</summary>
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
        return false;
    }
}
