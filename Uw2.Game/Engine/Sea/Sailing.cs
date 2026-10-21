using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 함대가 얼마나 빨리 가는가 — <c>KOUKAI2.EXE 0x00448520</c>(제 함대) 를 그대로 옮겼다.
/// </summary>
/// <remarks>
/// 배마다 빠르기를 내고 <b>가장 느린 배</b>가 함대 빠르기다. 정수 셈이고 나눗셈마다 버린다.
/// <code>
///   각    = |풍향 - 뱃머리|                          0~7, 8방위라 이것으로 둘레가 맞는다
///   v     = 돛효율표[각][돛 갈래] x 돛성능 x 풍속 / 150
///   v     = v x min(100, 선원 x 돛에 붙은 몫% / 드는 선원) / 100
///   v     = v x min(150, 180 - 짐%) / 100           빈 배 150% · 꽉 찬 배 80%
///   v     = min(30, v + v x 항해 레벨 / 10)           (그 배 선장의)
///   v     = min(40, v x 항해술 / 75)
///   맞바람(각 3~5)이면  v = v x 역풍성능 / 100
///   함대  = max(2, 가장 느린 배)                       바람이 0 이고 노 젓는 배가 없으면 0
/// </code>
/// 노 젓는 배(배 기록 +0x10 에 비트 0x10 이 없는 배)는 바람이 약해도 세기 3 으로 친다.
///
/// 빠르기는 <b>한 틱에 쌓는 걸음 수</b>다 — 함대는 칸 안 자리를 0~59 로 들고 다니다 60 을 넘으면 한 칸 간다
/// (<see cref="CellUnits"/>, <c>0x00448BC0</c>). 화면에는 <c>(빠르기+1)/2</c> 를 노트로 적는다(<c>0x0043410F</c>).
///
/// 남의 함대(<c>0x00418020</c>)는 셈이 조금 다르다 — 선원·짐을 안 보고 제독 하나의 능력만 쓰며,
/// 해류를 빠르기에 더하고 뺀다(<see cref="NpcFleet"/>).
/// </remarks>
public static class Sailing
{
    /// <summary>방위 가짓수.</summary>
    public const int DirCount = WindCurTable.DirCount;

    /// <summary>칸 하나가 이만큼의 걸음이다.</summary>
    public const int CellUnits = 60;

    /// <summary>함대 빠르기의 바닥.</summary>
    public const int MinSpeed = 2;

    /// <summary>항해 레벨을 얹은 뒤 자르는 값과 항해술을 얹은 뒤 자르는 값.</summary>
    public const int LevelCap = 30, MaxSpeed = 40;

    /// <summary>노 젓는 배가 바람이 약할 때 치는 세기.</summary>
    public const int OarWind = 3;

    /// <summary>
    /// 돛 효율 표 — <c>0x00456EA0</c>, 8 x 8. 줄이 <see cref="AngleOf">각</see>, 칸이 돛 갈래다.
    /// 남의 함대가 쓰는 <c>0x00458150</c> 도 값이 같다.
    /// </summary>
    private static readonly byte[,] Efficiency =
    {
        { 10, 8, 10, 9, 9, 8, 10, 10 },
        { 10, 9, 10, 10, 10, 10, 10, 10 },
        { 8, 9, 10, 10, 10, 10, 10, 10 },
        { 6, 7, 8, 9, 9, 10, 9, 10 },
        { 4, 6, 6, 7, 7, 8, 9, 10 },
        { 6, 7, 8, 9, 9, 10, 9, 10 },
        { 8, 9, 10, 10, 10, 10, 10, 10 },
        { 10, 9, 10, 10, 10, 10, 10, 10 },
    };

    /// <summary>바람과 뱃머리가 이루는 각. 0 이 순풍(바람이 뱃머리 쪽으로 민다), 4 가 정면 역풍이다.</summary>
    public static int AngleOf(int windDir, int heading) => Math.Abs((windDir & 7) - (heading & 7));

    /// <summary>각 3~5 — 역풍 성능을 곱하는 맞바람.</summary>
    public static bool IsHeadwind(int angle) => angle is >= 3 and <= 5;

    /// <summary>배 한 척의 빠르기.</summary>
    public static int ShipSpeed(in ShipSailing ship, int angle, int windSpeed)
    {
        int wind = ship.Oars ? Math.Max(OarWind, windSpeed) : windSpeed;
        int v = Efficiency[angle, ship.SailKind & 7] * ship.Sail * wind / 150;

        int crew = ship.RequiredCrew > 0 ? Math.Min(100, ship.Crew * ship.SailingShare / ship.RequiredCrew) : 100;
        v = v * crew / 100;

        int load = ship.Capacity > 0 ? Math.Min(150, 180 - ship.Load * 100 / ship.Capacity) : 150;
        v = v * load / 100;

        v = Math.Min(LevelCap, v + v * ship.NavLevel / 10);
        v = Math.Min(MaxSpeed, v * ship.Seamanship / 75);

        if (IsHeadwind(angle)) v = v * ship.Upwind / 100;
        return v;
    }

    /// <summary>
    /// 남 함대의 배 한 척 — <c>0x00418020</c>. 선원 · 짐 몫이 없고 제독의 항해 레벨 · 항해술을 쓴다.
    /// </summary>
    public static int AdmiralShipSpeed(int sailKind, int sail, int upwind, int angle, int windSpeed,
                                       int navLevel, int seamanship)
    {
        int v = Efficiency[angle, sailKind & 7] * sail * windSpeed / 150;
        v = Math.Min(LevelCap, v + v * navLevel / 10);
        v = Math.Min(MaxSpeed, v * seamanship / 75);
        if (IsHeadwind(angle)) v = v * upwind / 100;
        return v;
    }

    /// <summary>함대 빠르기 — 가장 느린 배. 바람이 자고 노 젓는 배가 없으면 0 이다.</summary>
    public static int FleetSpeed(IReadOnlyList<ShipSailing> ships, int heading, int windDir, int windSpeed)
    {
        if (ships.Count == 0) return 0;
        int angle = AngleOf(windDir, heading);
        int slowest = 0xFF;
        bool oars = false;
        foreach (var ship in ships)
        {
            oars |= ship.Oars;
            slowest = Math.Min(slowest, ShipSpeed(ship, angle, windSpeed));
        }
        slowest = Math.Max(MinSpeed, slowest);
        return windSpeed == 0 && !oars ? 0 : slowest;
    }

    /// <summary>화면에 적는 노트.</summary>
    public static int Knots(int speed) => (speed + 1) / 2;

    /// <summary>
    /// 칸 걸음들을 가는 데 드는 날 어림 — 걸음마다 그 칸 표 바람과 뱃머리로 함대 빠르기를 셈하고(돌풍 <see cref="Gust"/> 은 0 과 +1 의 가운데),
    /// 해류를 <c>0x00448BC0</c> 처럼 축마다 더해 칸 하나(60 걸음)에 드는 틱을 (끝수째) 모은 뒤 하루 틱으로 나눠 올림한다.
    /// 대각 걸음은 두 축 가운데 느린 쪽이 칸을 넘을 때다. 원본에는 없는 셈이다(항이동 보급 경고용) — 날씨는 안 본다.
    /// 배가 없으면 <see cref="int.MaxValue"/>.
    /// </summary>
    public static int EstimateDays(IReadOnlyList<ShipSailing> ships,
                                   IEnumerable<(int Heading, int WindDir, int WindSpeed, int CurDir, int CurSpeed)> steps)
    {
        if (ships.Count == 0) return int.MaxValue;
        double ticks = 0;
        foreach (var (heading, windDir, windSpeed, curDir, curSpeed) in steps)
        {
            // 빠르기 x2 로 셈해 돌풍 반 칸을 버리지 않는다.
            int speed2 = FleetSpeed(ships, heading, windDir, windSpeed) + FleetSpeed(ships, heading, windDir, Math.Min(7, windSpeed + 1));
            var (hx, hy) = WindCurTable.Vector(heading);
            var (cx, cy) = WindCurTable.Vector(curDir);
            int progress2 = int.MaxValue;
            if (hx != 0) progress2 = Math.Min(progress2, speed2 + 2 * hx * cx * curSpeed);
            if (hy != 0) progress2 = Math.Min(progress2, speed2 + 2 * hy * cy * curSpeed);
            progress2 = Math.Max(1, progress2);   // 거스르는 해류가 더 세도 언젠가는 바람이 바뀐다 — 아주 느리게 친다
            ticks += 2.0 * CellUnits / progress2;
        }
        return (int)Math.Ceiling(ticks / Fleet.TicksPerDay);
    }

    /// <summary>
    /// 표 바람을 흔든다 — <c>0x00418E80</c>. 세기는 0~1 을 더해 7 에서 자르고, 방위는 열다섯에 한 번꼴로
    /// 한 칸 돈다(<c>rand(5)/4 x (rand(3)-1)</c>).
    /// </summary>
    /// <remarks>
    /// 폭풍 · 무풍 같은 날씨가 걸려 있으면 게임은 흔들지 않고 지난 바람을 그대로 둔다(<see cref="Fleet"/> 가 가른다).
    /// </remarks>
    public static (int Dir, int Speed) Gust((int Dir, int Speed) table, Random rng)
    {
        int speed = Math.Min(7, table.Speed + rng.Next(2));
        int turn = rng.Next(5) / 4 * (rng.Next(3) - 1);
        return ((table.Dir + turn) & 7, speed);
    }
}

/// <summary>
/// 배 한 척의 빠르기 셈에 드는 값. 게임에서는 세 군데에 흩어져 있다.
/// </summary>
/// <remarks>
/// <code>
///   함대 칸(9바이트, 함대+0x2B)   +0 선원(낱말) · +4 역풍성능 · +5 돛성능 · +7 배 번호
///   배 기록(22바이트, 0x00461560)  +0x0F 선종 · +0x10 비트 0x10 이면 돛만 쓰는 배
///   제 함대 칸 곁(30바이트, 0x004610B0)  +9 돛에 붙은 선원 몫% · +0x0A 짐(낱말) · +0x1B 선장
/// </code>
/// 돛에 붙은 몫은 2편의 선원 나누기(항해 · 전투 · 포술)다 — 싸움 셈은 <c>100 - (+9) - (+8)</c> 을 쓴다(<c>0x00410475</c>).
/// </remarks>
public readonly record struct ShipSailing(
    int SailKind, int Sail, int Upwind, bool Oars,
    int Crew, int SailingShare, int RequiredCrew,
    int Load, int Capacity,
    int NavLevel, int Seamanship)
{
    public const int DefaultSailingShare = 50;

    /// <summary>
    /// 선종 표 한 줄과 선장으로 새 배를 띄운다 — 선원을 다 채우고 짐은 비운다. 돛 몫은 원본 새 배 채비대로 50%
    /// (칸 곁 +9 = 0x32, +8 = 0x19 — <c>0x0043DF87</c> · <c>0x0040B920</c>)라 드는 선원의 곱절을 태워야 돛이 다 산다.
    /// </summary>
    public static ShipSailing Fresh(StartData.ShipType type, StartData.Person captain, bool oars = false) =>
        new(type.SailKind, type.Sail, type.Upwind, oars,
            Crew: Math.Max(type.RequiredCrew, type.MaxCrew), SailingShare: DefaultSailingShare, type.RequiredCrew,
            Load: 0, type.Capacity, captain.NavLevel, captain.Seamanship);
}
