namespace Uw2.Support.Local.Formats;

/// <summary>
/// 바람·해류 표. <c>WINDCUR.DAT</c> 1,350바이트는 <b>450바이트 표 세 장</b>이다.
/// </summary>
/// <remarks>
/// 게임이 파일에서 <b>450바이트씩</b> 읽어 간다(<c>KOUKAI2.EXE 0x00415BDF</c> · <c>0x0042B771</c>).
/// 어느 장을 읽을지는 <c>0x004703DC</c>(달) 이 가른다.
/// <code>
///   3 &lt;= 달 &lt; 9  ->  자리 0     (0장)
///   그 밖         ->  자리 0x1C2 (1장)
/// </code>
/// 곧 <b>0장·1장이 철 따라 갈리는 바람</b>이고, 2장은 <b>해류</b>다 — 값의 결이 다르다
/// (해류 장에만 방위 0 이 백 번 나오고 6·7·14·15 가 아예 없다). 3편도 표를 세 장
/// (1~6월 바람 · 7~12월 바람 · 해류) 뒀다(<c>[[16.분석-풍향·풍속·해류]]</c>).
///
/// <para>격자는 30 x 15 다</para>
/// 표를 짚는 손(<c>0x0041E270</c>)이 이렇게 셈한다.
/// <code>
///   열 = 함대x / 72        (0~29)
///   행 = 함대y / 72        (0~14)
///   색인 = 행 * 30 + 열
/// </code>
/// 함대 좌표는 <b>칩 눈금</b>(0~2159, 0~1079)이라 한 칸이 <b>72 x 72 칩 = 36 x 36 지도칸
/// = 경위도 12도</b>다. 네모반듯하다.
///
/// <para>바이트 한 칸</para>
/// <code>
///   비트 0..3   방위 0~15
///   비트 4..5   세기 0~3
///   비트 6·7    깃발 — 아직 뜻을 모른다
/// </code>
/// 방위가 "불어오는 쪽"인지 "밀어가는 쪽"인지는 아직 안 굳혔다. 3편은 <b>밀어가는 쪽</b>이었다.
/// </remarks>
public sealed class WindCurTable
{
    /// <summary>표 한 장의 크기.</summary>
    public const int Cols = 30, Rows = 15, PageBytes = Cols * Rows;   // 450

    /// <summary>표 장 수.</summary>
    public const int Pages = 3;

    /// <summary>한 칸이 덮는 칩 수. 함대 좌표를 이것으로 나눠 칸을 고른다.</summary>
    public const int ChipsPerCell = 72;

    /// <summary>방위 가짓수. 3편과 같이 열여섯이다.</summary>
    public const int DirCount = 16;

    /// <summary>철이 갈리는 달. 3~8월이 <see cref="Page.WindWarm"/>, 나머지가 <see cref="Page.WindCold"/> 다.</summary>
    public const int WarmFirstMonth = 3, WarmLastMonth = 8;

    /// <summary>표 세 장.</summary>
    public enum Page
    {
        /// <summary>3~8월 바람.</summary>
        WindWarm = 0,
        /// <summary>9~2월 바람.</summary>
        WindCold = 1,
        /// <summary>해류. 철을 안 탄다.</summary>
        Current = 2,
    }

    private readonly byte[] _raw;

    private WindCurTable(byte[] raw) => _raw = raw;

    /// <summary>파일 원본 1,350바이트.</summary>
    public byte[] Raw => _raw;

    /// <summary>날바이트에서 바로 만든다.</summary>
    public static WindCurTable FromBytes(byte[] raw)
    {
        if (raw.Length != PageBytes * Pages)
            throw new ArgumentException($"{PageBytes * Pages}바이트가 아닙니다 ({raw.Length})");
        return new WindCurTable(raw);
    }

    /// <summary>파일에서 읽는다.</summary>
    public static WindCurTable Load(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length != PageBytes * Pages)
            throw new InvalidDataException($"WINDCUR.DAT 이 {PageBytes * Pages}바이트가 아닙니다 ({d.Length})");
        return new WindCurTable(d);
    }

    /// <summary>그 달에 쓸 바람 장.</summary>
    public static Page WindPageOf(int month) =>
        month >= WarmFirstMonth && month <= WarmLastMonth ? Page.WindWarm : Page.WindCold;

    /// <summary>표 한 칸의 날바이트. <paramref name="chipX"/>·<paramref name="chipY"/> 는 <b>칩 눈금</b>이다.</summary>
    public byte At(Page page, int chipX, int chipY)
    {
        int c = Math.Clamp(chipX / ChipsPerCell, 0, Cols - 1);
        int r = Math.Clamp(chipY / ChipsPerCell, 0, Rows - 1);
        return _raw[(int)page * PageBytes + r * Cols + c];
    }

    /// <summary>방위(0~15)와 세기(0~3).</summary>
    public (int Dir, int Speed) Flow(Page page, int chipX, int chipY)
    {
        byte b = At(page, chipX, chipY);
        return (b & 0x0F, (b >> 4) & 0x03);
    }

    /// <summary>그 달의 바람.</summary>
    public (int Dir, int Speed) Wind(int month, int chipX, int chipY) =>
        Flow(WindPageOf(month), chipX, chipY);

    /// <summary>해류.</summary>
    public (int Dir, int Speed) Current(int chipX, int chipY) =>
        Flow(Page.Current, chipX, chipY);

    /// <summary>
    /// 방위의 (dx, dy). 0 이 북, 시계 반대로 도는 3편 <c>WindTable.Vector</c> 와 같은 얼개다.
    /// <b>2편의 도는 쪽은 아직 안 굳혔다</b> — 화살표를 지도에 얹어 보고 맞춰야 한다.
    /// </summary>
    public static (double X, double Y) Vector(int dir)
    {
        double a = -Math.PI / 2 + dir * (2 * Math.PI / DirCount);
        return (Math.Cos(a), Math.Sin(a));
    }
}
