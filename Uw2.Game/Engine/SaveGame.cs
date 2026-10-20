using System.IO;
using System.Text.Json;

namespace Uw2.Game.Engine;

/// <summary>
/// 세이브 한 칸 — 우리 꼴(JSON)이다. 원본 <c>KOUKAI2.DAT</c>(0x97 머리 + 0x837B x 10)과는 다르다.
/// </summary>
/// <remarks>
/// 원본 세이브가 기억을 통째로 쓰듯, 시나리오 기억(<see cref="ScenarioMemory"/>)의 표 스물을 그대로 담는다 —
/// 사람 · 동료 · 중고선 · 주인공 표가 다 거기 있다. 항구 형편 백은 교역 표 날바이트로 따로 담는다.
/// </remarks>
public sealed class SaveGame
{
    public const int Slots = 10;

    public int Hero { get; set; }
    public int Port { get; set; } = -1;
    public int HomePort { get; set; }
    public string Summary { get; set; } = "";
    public DateTime SavedAt { get; set; }

    public FleetState Fleet { get; set; } = new();
    public PlayerData Player { get; set; } = new();
    public ScenarioVm.State? Scenario { get; set; }

    /// <summary>함께 쓰는 시나리오(SNR0)의 기계 상태 — 기억은 주인공 것과 같다.</summary>
    public ScenarioVm.State? Common { get; set; }
    public byte[] PortStates { get; set; } = [];

    /// <summary>지리 조사 판(<see cref="Sea.Survey"/>). 옛 세이브는 없다 — 그때는 새 게임 판이다.</summary>
    public SurveyState? Survey { get; set; }

    public sealed class SurveyState
    {
        public string Cells { get; set; } = "";
        public int Total { get; set; }
        public int SinceReport { get; set; }
    }

    public sealed class FleetState
    {
        public int CellX { get; set; }
        public int CellY { get; set; }
        public int SubX { get; set; }
        public int SubY { get; set; }
        public int Heading { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int TimeOfDay { get; set; }
        public int DaysAtSea { get; set; }
    }

    public sealed class PlayerData
    {
        public int Gold { get; set; }
        public int Balance { get; set; }
        public int WaterRate { get; set; } = PlayerState.FullRation;
        public int FoodRate { get; set; } = PlayerState.FullRation;
        public bool Rats { get; set; }
        public bool Scurvy { get; set; }
        public List<ShipData> Ships { get; set; } = [];

        /// <summary>도크에 맡긴 배 — <see cref="ShipData.Port"/> 가 맡긴 항구다.</summary>
        public List<ShipData> Docked { get; set; } = [];
    }

    public sealed class ShipData
    {
        public int Type { get; set; }
        public string Name { get; set; } = "";
        public int Room { get; set; }
        public int[] Supplies { get; set; } = new int[PlayerState.SupplyKinds];
        public int[] CargoGoods { get; set; } = [];
        public int[] CargoAmounts { get; set; } = [];
        public int Crew { get; set; }
        public int MaxCrew { get; set; }
        public int Hull { get; set; }
        public int MaxHull { get; set; }
        public int Upwind { get; set; }
        public int Sail { get; set; }
        public int Grade { get; set; }
        public int Captain { get; set; } = -1;
        public int Morale { get; set; } = PlayerState.DefaultMorale;
        public int Port { get; set; } = -1;
    }

    public static PlayerData From(PlayerState p) => new()
    {
        Gold = p.Gold, Balance = p.Balance, WaterRate = p.WaterRate, FoodRate = p.FoodRate, Rats = p.Rats, Scurvy = p.Scurvy,
        Ships = [.. p.Ships.Select(s => Of(s, -1))],
        Docked = [.. p.Docked.Select(d => Of(d.Ship, d.Port))],
    };

    private static ShipData Of(PlayerState.Ship s, int port) => new()
    {
        Type = s.Type, Name = s.Name, Room = s.Room, Supplies = [.. s.Supplies],
        CargoGoods = [.. s.Cargo.Select(c => c.Good)], CargoAmounts = [.. s.Cargo.Select(c => c.Amount)],
        Crew = s.Crew, MaxCrew = s.MaxCrew, Hull = s.Hull, MaxHull = s.MaxHull, Upwind = s.Upwind, Sail = s.Sail,
        Grade = s.Grade, Captain = s.Captain, Morale = s.Morale, Port = port,
    };

    private static PlayerState.Ship ToShip(ShipData s)
    {
        var ship = new PlayerState.Ship
        {
            Type = s.Type, Name = s.Name, Room = s.Room, MaxCrew = s.MaxCrew, Crew = s.Crew,
            Hull = s.Hull, MaxHull = s.MaxHull, Upwind = s.Upwind, Sail = s.Sail, Grade = s.Grade, Captain = s.Captain, Morale = s.Morale,
        };
        for (int k = 0; k < PlayerState.SupplyKinds && k < s.Supplies.Length; k++) ship.Supplies[k] = s.Supplies[k];
        for (int k = 0; k < PlayerState.CargoSlots && k < s.CargoGoods.Length && k < s.CargoAmounts.Length; k++)
            ship.Cargo[k] = (s.CargoGoods[k], s.CargoAmounts[k]);
        return ship;
    }

    public static PlayerState ToPlayer(PlayerData d)
    {
        var p = new PlayerState(d.Gold) { WaterRate = d.WaterRate, FoodRate = d.FoodRate, Rats = d.Rats, Scurvy = d.Scurvy };
        p.Transfer(d.Balance);          // 돈 → 잔고로 옮긴 꼴이라 돈은 다시 매긴다
        p.SetGold(d.Gold);
        foreach (var s in d.Ships) p.Ships.Add(ToShip(s));
        foreach (var s in d.Docked) p.Docked.Add((s.Port, ToShip(s)));
        return p;
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>세이브 폴더 — <c>%LOCALAPPDATA%\Uw2\saves</c>.</summary>
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Uw2", "saves");

    public static string PathOf(int slot) => Path.Combine(Folder, $"slot{slot + 1}.json");

    public void Write(int slot)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathOf(slot), JsonSerializer.Serialize(this, Json));
    }

    public static SaveGame? Read(int slot)
    {
        try { return File.Exists(PathOf(slot)) ? JsonSerializer.Deserialize<SaveGame>(File.ReadAllText(PathOf(slot)), Json) : null; }
        catch (Exception e) when (e is JsonException or IOException) { return null; }
    }

    /// <summary>칸 고르기에 늘어놓을 글 — 빈 칸은 「(비었음)」.</summary>
    public static List<string> SlotLines() =>
        [.. Enumerable.Range(0, Slots).Select(i => Read(i) is { } s ? $"{i + 1,2}  {s.Summary}" : $"{i + 1,2}  (비었음)")];
}
