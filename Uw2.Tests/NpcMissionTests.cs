using Uw2.Game.Engine.Sea;
using Uw2.Support.Local.Formats;
using Xunit;

namespace Uw2.Tests;

/// <summary>남 함대 할 일 — 구운 자산으로 두 달을 돌려 본다.</summary>
public class NpcMissionTests
{
    private sealed class World(StartData start, IReadOnlyList<PortTable.Port> ports, Economy economy) : NpcFleet.IWorld
    {
        public List<NpcFleet> Fleets { get; } = [];
        public (int X, int Y)? PortPos(int port) => port >= 0 && port < ports.Count ? (ports[port].X, ports[port].Y) : null;
        public int Nation(int nation, int field) => start.NationBytes[nation * StartData.NationSize + field];
        public int NationPower(int nation) => 300;
        public Economy? Economy => economy;
        public int HeroRank => 1;
        public int HeroNation => 0;
        public int HeroFleetIndex => 0;
        public int HeroAdmiral => 0;
        public bool HeroAtSea => true;
        public (int X, int Y) HeroCell => (120, 358);
        public NpcFleet? FleetByIndex(int index) => Fleets.FirstOrDefault(f => f.Index == index);
    }

    [Fact]
    public void Two_months_of_missions_run()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, AssetPack.FolderName))) dir = dir.Parent;
        var pack = AssetPack.Load(Path.Combine(dir!.FullName, AssetPack.FolderName))!;
        var start = pack.Start!;
        var sea = new Fleet(WorldMap.FromCells(pack.Cells), pack.ToChipMap(), null);
        var world = new World(start, pack.Ports, pack.Economy!.Clone());
        var rng = new Random(3);
        foreach (var f in start.Fleets.Where(f => f.Active && f.ShipCount > 0 && f.Admiral != 0xFF))
        {
            var home = f.Home < pack.Ports.Count ? (pack.Ports[f.Home].X, pack.Ports[f.Home].Y) : (f.TargetX, f.TargetY);
            var npc = new NpcFleet(sea, pack.Routes!, rng, f, start, home, (f.TargetX, f.TargetY)) { World = world };
            if (!sea.CanSail(f.X, f.Y)) npc.StayInPort(rng.Next(8) + 8);
            world.Fleets.Add(npc);
        }
        Assert.True(world.Fleets.Count > 30);
        var modes = new HashSet<int>();
        for (int day = 0; day < 60; day++)
        {
            for (int t = 0; t < Fleet.TicksPerDay; t++)
                foreach (var n in world.Fleets) { n.Step(((day * 3 + t / 24) % 8, 4), (0, 0)); n.MeetsHero(30); }
            foreach (var n in world.Fleets) { n.NewDay(); modes.Add(n.Mode); }
        }
        Assert.Contains(world.Fleets, n => n.Mode != 0 || n.Gold > 0);
        Assert.True(modes.Count > 3, $"갈래가 모자라다: {string.Join(",", modes)}");
        Assert.Contains(modes, m => m is 1 or 2);          // 상선이 투자 · 사들이기에 나선다
        Assert.Contains(modes, m => m is >= 4 and <= 8);   // 해군 · 해적이 누군가를 쫓는다
    }

    [Fact]
    public void State_round_trips_through_json()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, AssetPack.FolderName))) dir = dir.Parent;
        var pack = AssetPack.Load(Path.Combine(dir!.FullName, AssetPack.FolderName))!;
        var start = pack.Start!;
        var sea = new Fleet(WorldMap.FromCells(pack.Cells), pack.ToChipMap(), null);
        var f = start.Fleets[1];
        var a = new NpcFleet(sea, pack.Routes!, new Random(1), f, start, (f.TargetX, f.TargetY), (f.TargetX, f.TargetY))
        {
            Mode = 5, Goal = 42, Gold = 77,
        };
        a.Slots.RemoveAt(0);
        string json = System.Text.Json.JsonSerializer.Serialize(a.Export());
        var b = new NpcFleet(sea, pack.Routes!, new Random(1), f, start, (f.TargetX, f.TargetY), (f.TargetX, f.TargetY));
        b.Restore(System.Text.Json.JsonSerializer.Deserialize<NpcFleet.State>(json)!);
        Assert.Equal((5, 42, 77), (b.Mode, b.Goal, b.Gold));
        Assert.Equal(a.Slots, b.Slots);
        Assert.Equal((a.X, a.Y), (Math.Floor(b.X) + 0.5, Math.Floor(b.Y) + 0.5));
    }
}
