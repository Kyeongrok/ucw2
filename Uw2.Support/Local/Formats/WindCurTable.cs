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
/// (해류 장에만 세기 0 이 백열두 번 나오고 6·7 이 아예 없다). 3편도 표를 세 장
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
/// 바람을 흔드는 손(<c>0x00418E80</c>)과 바람·해류 그림을 찍는 손(<c>0x004366D0</c> → <c>0x00436740</c>)이
/// 이렇게 가른다.
/// <code>
///   비트 0..2   세기 0~7     바람 장에는 0 이 없다(늘 분다). 해류는 0~5
///   비트 3..5   방위 0~7     0 이 북, 시계 방향. 바람·해류가 <b>밀어가는 쪽</b>이다
///   비트 6·7    날씨 깃발    폭풍이 이는 바다 따위 — 흔드는 손이 이 두 비트만 표에서 새로 가져온다
/// </code>
/// 방위가 밀어가는 쪽인 것은 셈에서 바로 보인다 — 뱃머리와 방위가 같으면 돛 효율 표의 0줄(가장 높다)을
/// 쓰고, 해류는 그 방위의 (dx, dy) 에 세기를 곱해 그대로 더한다(<c>0x00448BC0</c>).
/// </remarks>
public sealed class WindCurTable
{
    /// <summary>표 한 장의 크기.</summary>
    public const int Cols = 30, Rows = 15, PageBytes = Cols * Rows;   // 450

    /// <summary>표 장 수.</summary>
    public const int Pages = 3;

    /// <summary>한 칸이 덮는 칩 수. 함대 좌표를 이것으로 나눠 칸을 고른다.</summary>
    public const int ChipsPerCell = 72;

    /// <summary>방위 가짓수. 3편(열여섯)과 달리 <b>여덟</b>이다.</summary>
    public const int DirCount = 8;

    /// <summary>방위마다 칸 한 걸음 (dx, dy) — <c>0x00456EE0</c>(dx) · <c>0x00456EE8</c>(dy). 0 이 북, 시계 방향.</summary>
    public static ReadOnlySpan<sbyte> Dx => [0, 1, 1, 1, 0, -1, -1, -1];
    public static ReadOnlySpan<sbyte> Dy => [-1, -1, 0, 1, 1, 1, 0, -1];

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

    /// <summary>방위(0~7)와 세기(0~7).</summary>
    public (int Dir, int Speed) Flow(Page page, int chipX, int chipY) => Split(At(page, chipX, chipY));

    /// <summary>표 바이트 하나를 방위와 세기로 가른다.</summary>
    public static (int Dir, int Speed) Split(byte b) => ((b >> 3) & 7, b & 7);

    /// <summary>그 달의 바람.</summary>
    public (int Dir, int Speed) Wind(int month, int chipX, int chipY) =>
        Flow(WindPageOf(month), chipX, chipY);

    /// <summary>해류.</summary>
    public (int Dir, int Speed) Current(int chipX, int chipY) =>
        Flow(Page.Current, chipX, chipY);

    /// <summary>방위의 (dx, dy). 대각선도 두 축으로 한 칸씩이다 — 게임은 길이를 맞추지 않는다.</summary>
    public static (int X, int Y) Vector(int dir) => (Dx[dir & 7], Dy[dir & 7]);
}
