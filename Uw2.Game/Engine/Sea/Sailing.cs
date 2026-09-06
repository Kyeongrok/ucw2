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
    /// 임시 셈. 뱃머리와 바람이 이루는 각으로 돛 효율을 어림한다.
    /// </summary>
    /// <param name="windDir">풍향(16방위).</param>
    /// <param name="windSpeed">풍속.</param>
    /// <param name="heading">뱃머리(16방위).</param>
    /// <param name="thrust">추진력. 배 표를 읽기 전까지의 어림값이다.</param>
    public static double SpeedOf(int windDir, int windSpeed, int heading, int thrust = 20)
    {
        int rel = ((windDir - heading) & 0xF);
        // 뒤에서 불면 1, 옆이면 0.6, 앞이면 0.15 — 삼각돛도 정면 역풍에서 조금은 나간다.
        double cos = Math.Cos(rel * (2 * Math.PI / 16));
        double eff = 0.15 + 0.85 * (cos + 1) / 2;
        double v = thrust * (windSpeed + 1) * eff / 100.0;
        return Math.Max(CalmSpeed, v);
    }

    /// <summary>속도를 한 틱에 나아갈 칸 수로.</summary>
    public static double CellsPerTick(double speed) => speed / StepsPerCell;
}
