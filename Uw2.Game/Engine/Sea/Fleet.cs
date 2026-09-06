using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 바다 위의 함대 하나. 자리·뱃머리·빠르기를 들고 틱마다 나아간다.
/// </summary>
/// <remarks>
/// 자리는 <b>칩 눈금</b>이다(0~2159 / 0~1079) — 괴물표도 항구표도 바람표도 다 이 눈금이라
/// 셈이 한 자로 맞는다.
///
/// <b>빠르기 셈은 아직 게임 것이 아니다.</b> <see cref="Sailing"/> 이 3편 식을 임시로
/// 꽂아 둔 자리라, 눈금이 달라 게임과 견주면 안 된다. 얼개(바람으로 빠르기를 내고, 해류가
/// 옆으로 밀고, 뭍에 막히면 미끄러진다)는 게임과 같다.
/// </remarks>
public sealed class Fleet
{
    /// <summary>
    /// 한 틱에 흐르는 시간(초). 게임은 하루가 마흔여덟 틱이니 이 값이면 하루가 열 초 남짓이다.
    /// </summary>
    public const double TickSeconds = 0.2;

    /// <summary>방위 가짓수.</summary>
    public const int DirCount = WindCurTable.DirCount;

    private readonly ChipMap _chips;
    private readonly WorldMap _map;
    private readonly WindCurTable? _wind;

    public Fleet(WorldMap map, ChipMap chips, WindCurTable? wind)
    {
        _map = map; _chips = chips; _wind = wind;
    }

    /// <summary>지금 자리(칩 눈금).</summary>
    public double X { get; private set; }
    public double Y { get; private set; }

    /// <summary>뱃머리. 0 이 북이고 열여섯 방위다.</summary>
    public int Heading { get; set; }

    /// <summary>돛을 폈는지. 접으면 그 자리에 선다.</summary>
    public bool UnderSail { get; set; } = true;

    /// <summary>달. 바람 표가 철 따라 갈린다.</summary>
    public int Month { get; set; } = 4;

    /// <summary>지난 틱에 잰 것들 — 상태줄에 적으려고 남긴다.</summary>
    public (int Dir, int Speed) LastWind { get; private set; }
    public (int Dir, int Speed) LastCurrent { get; private set; }
    public double LastSpeed { get; private set; }

    /// <summary>뭍에 막혀 못 간 틱이면 참.</summary>
    public bool Blocked { get; private set; }

    /// <summary>자리를 옮겨 놓는다. 항구에서 나올 때 쓴다.</summary>
    public void PlaceAt(double x, double y)
    {
        X = x; Y = y;
        Blocked = false;
    }

    /// <summary>
    /// 그 자리에서 가장 가까운 바다 칸을 찾는다. 항구는 뭍에 붙어 있어 그대로는 못 뜬다.
    /// </summary>
    public bool PlaceNearSea(double x, double y, int reach = 40)
    {
        for (int r = 0; r <= reach; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    double nx = x + dx, ny = y + dy;
                    if (CanSail(nx, ny)) { PlaceAt(nx, ny); return true; }
                }
        return false;
    }

    /// <summary>그 칩 자리를 배가 지날 수 있는지.</summary>
    public bool CanSail(double chipX, double chipY)
    {
        if (chipY < 0 || chipY >= ChipMap.Height) return false;
        int cx = Wrap((int)Math.Floor(chipX));
        int cy = (int)Math.Floor(chipY);

        var (lt, rt, lb, rb) = _chips.Quad(_map[cx / ChipMap.PerCell, cy / ChipMap.PerCell]);
        byte chip = (cx & 1) == 0 ? ((cy & 1) == 0 ? lt : lb)
                                  : ((cy & 1) == 0 ? rt : rb);
        return ChipMap.CanSail(chip);
    }

    private static int Wrap(int x) => ((x % ChipMap.Width) + ChipMap.Width) % ChipMap.Width;

    /// <summary>
    /// 한 틱 나아간다. 뭍에 막히면 <b>가로·세로 하나만 살려 미끄러진다</b> — 해안을 따라
    /// 붙어 갈 수 있게 하는 것이고, 게임도 이렇게 군다.
    /// </summary>
    public void Step()
    {
        Blocked = false;
        if (!UnderSail) { LastSpeed = 0; return; }

        int wx = Wrap((int)X), wy = Math.Clamp((int)Y, 0, ChipMap.Height - 1);
        LastWind = _wind?.Wind(Month, wx, wy) ?? (0, 0);
        LastCurrent = _wind?.Current(wx, wy) ?? (0, 0);

        double speed = Sailing.SpeedOf(LastWind.Dir, LastWind.Speed, Heading);
        LastSpeed = speed;

        // 칸 눈금으로 낸 값을 칩 눈금으로 바꾼다(칸 하나가 칩 둘).
        double step = Sailing.CellsPerTick(speed) * ChipMap.PerCell;
        var (hx, hy) = WindCurTable.Vector(Heading);

        var (cx2, cy2) = WindCurTable.Vector(LastCurrent.Dir);
        double drift = LastCurrent.Speed / 8.0 / Sailing.StepsPerCell * ChipMap.PerCell;

        double dx = hx * step + cx2 * drift;
        double dy = hy * step + cy2 * drift;

        // 위도가 높을수록 경도 한 칸이 짧다 — 3편과 같은 보정이다.
        dx *= Sailing.LonScale(90 - Y / ChipMap.Height * 180);

        if (CanSail(X + dx, Y + dy)) { X = X + dx; Y = Y + dy; }
        else if (CanSail(X + dx, Y)) { X += dx; Blocked = true; }
        else if (CanSail(X, Y + dy)) { Y += dy; Blocked = true; }
        else Blocked = true;

        X = ((X % ChipMap.Width) + ChipMap.Width) % ChipMap.Width;
        Y = Math.Clamp(Y, 0, ChipMap.Height - 1);
    }

    /// <summary>뱃머리를 한 칸 돌린다.</summary>
    public void Turn(int by) => Heading = ((Heading + by) % DirCount + DirCount) % DirCount;

    /// <summary>방위 이름. 열여섯을 여덟으로 깎아 적는다.</summary>
    public static string DirName(int dir) =>
        new[] { "북", "북북동", "북동", "동북동", "동", "동남동", "남동", "남남동",
                "남", "남남서", "남서", "서남서", "서", "서북서", "북서", "북북서" }
        [((dir % DirCount) + DirCount) % DirCount];
}
