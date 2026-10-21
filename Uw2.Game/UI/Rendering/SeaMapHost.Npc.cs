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
        Nations.NewMonth(m, ports, n => m.Read(ScenarioMemory.At(ScenarioMemory.HeroTable, hero, 6 + n), false), HeroNation, Roll);
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
