namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 함대가 얼마나 빨리 가는가. <b>아직 2편 것을 못 캤다</b> — 3편 셈을 임시로 꽂아 둔 자리다.
/// </summary>
/// <remarks>
/// 3편은 이랬다(<c>CDS_95.EXE 0x0048BCF0</c>, 볼트 <c>30.분석-항해 속도(돛·바람·해류)</c>).
/// <code>
///   배속도  = 추진력 x (풍속+1) x 돛효율 / 100
///   함대속도 = (기함속도 + 평균) / 2
///   누산기가 0x40 마다 한 걸음, 한 칸이 열여섯 걸음
/// </code>
/// 2편 것은 <c>KOUKAI2.EXE</c> 에서 다시 캐야 한다. 캐기 전까지는 <b>바람 세기만 보는
/// 임시 셈</b>으로 화면을 돌린다 — 눈금이 다르므로 <b>이 값으로 게임과 견주면 안 된다</b>.
///
/// 캘 때 볼 곳은 볼트 <c>Project/uw2/분석/6.분석-KOUKAI2.EXE 뜯는 자리</c>.
/// </remarks>
public static class Sailing
{
    /// <summary>누산기가 한 걸음을 넘기는 값. 3편 값이다.</summary>
    public const int CellUnits = 64;

    /// <summary>한 칸에 든 걸음 수. 3편 값이다 — <b>2편은 다시 재야 한다</b>.</summary>
    public const int StepsPerCell = 16;

    /// <summary>바람이 없을 때의 바닥 속도.</summary>
    public const int CalmSpeed = 1;

    /// <summary>
    /// 돛 효율의 아래위(백분). 정면 역풍에서도 삼각돛이 조금은 나아간다.
    /// </summary>
    public const int MinEfficiency = 25, MaxEfficiency = 100;

    /// <summary>어림 추진력. 배 표를 읽기 전까지 쓰는 값이다.</summary>
    public const int DefaultThrust = 40;

    /// <summary>
    /// 눈금 맞춤. <b>우리가 고른 값</b>이다 — 게임 셈을 캐면 없어질 자리다.
    /// </summary>
    /// <remarks>
    /// 3편 식은 칸 눈금이 2편과 달라 그대로 쓰면 배가 화면 밖으로 튄다. 순풍에 한 틱에
    /// 한 칸 남짓 나아가도록 이 값으로 눌러 두었다 — 세계지도를 가로지르는 데 서너 분이다.
    /// </remarks>
    public const double PaceScale = 10;

    /// <summary>
    /// 임시 셈. 뱃머리와 바람이 이루는 각으로 돛 효율을 어림한다.
    /// </summary>
    /// <param name="windDir">풍향(16방위).</param>
    /// <param name="windSpeed">풍속.</param>
    /// <param name="heading">뱃머리(16방위).</param>
    /// <param name="thrust">추진력. 배 표를 읽기 전까지의 어림값이다.</param>
    public static double SpeedOf(int windDir, int windSpeed, int heading, int thrust = DefaultThrust)
    {
        // 뒤에서 불면 100, 옆이면 60 남짓, 정면 역풍이면 25 다(백분).
        int rel = (windDir - heading) & 0xF;
        double cos = Math.Cos(rel * (2 * Math.PI / DirCount));
        double eff = MinEfficiency + (MaxEfficiency - MinEfficiency) * (cos + 1) / 2;

        double v = thrust * (windSpeed + 1) * eff / 100.0;
        return Math.Max(CalmSpeed, v);
    }

    /// <summary>방위 가짓수.</summary>
    public const int DirCount = 16;

    /// <summary>속도를 한 틱에 나아갈 칸 수로.</summary>
    public static double CellsPerTick(double speed) => speed / StepsPerCell / PaceScale;

    /// <summary>
    /// 경도 보정 — 위도가 높을수록 경도 한 칸이 짧다. 3편 것을 그대로 옮겼다.
    /// </summary>
    /// <remarks>
    /// 3편은 <c>65536*cos</c> 표를 63도에서 자르고 보간해 쓴다. 여기서는 <c>1 / cos</c> 을
    /// 그냥 쓴다 — 어긋남이 0.5% 라 굳이 옮기지 않았다.
    /// <b>2편도 이렇게 하는지는 아직 안 굳혔다.</b>
    /// </remarks>
    public static double LonScale(double latitudeDegrees)
    {
        double capped = Math.Min(Math.Abs(latitudeDegrees), MaxLatForScale);
        return 1.0 / Math.Max(0.25, Math.Cos(capped * Math.PI / 180.0));
    }

    /// <summary>경도 보정을 자르는 위도.</summary>
    public const double MaxLatForScale = 63;
}
