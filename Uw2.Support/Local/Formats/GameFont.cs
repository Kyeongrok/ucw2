namespace Uw2.Support.Local.Formats;

/// <summary>
/// 게임 글꼴. <c>HANKAKU.FNT</c> 는 <b>8 x 16 한 비트 글자 아흔여섯 자</b>다(ASCII 0x20~0x7F).
/// </summary>
/// <remarks>
/// 한 글자가 열여섯 바이트, 한 줄이 한 바이트이고 <b>왼쪽 점이 최상위 비트</b>다.
/// 이 글꼴이 게임 첫 메뉴(<c>Load Data</c> / <c>Start New Game</c> …)에 쓰인 그것이다.
///
/// 한글·일본어 두 바이트 글자는 <c>ALL_FONT.16P</c>(104,340바이트) 쪽인데 아직 안 뜯었다.
/// </remarks>
public sealed class GameFont
{
    /// <summary>글자 한 칸.</summary>
    public const int Width = 8, Height = 16;

    /// <summary>담긴 글자 수.</summary>
    public const int Count = 96;

    /// <summary>첫 글자의 ASCII 값.</summary>
    public const char First = ' ';

    /// <summary>글자를 늘어놓을 때 한 줄에 몇 자씩 놓는지.</summary>
    public const int SheetCols = 16;

    private readonly byte[] _rows;   // [글자 * 16 + 줄]

    private GameFont(byte[] rows) => _rows = rows;

    /// <summary>파일에서 읽는다.</summary>
    public static GameFont Load(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>날바이트에서 읽는다.</summary>
    public static GameFont FromBytes(byte[] raw)
    {
        if (raw.Length < Count * Height)
            throw new InvalidDataException($"글꼴이 {Count * Height}바이트보다 짧습니다 ({raw.Length})");
        return new GameFont(raw);
    }

    /// <summary>글자 <paramref name="c"/> 의 줄 <paramref name="y"/> 비트들. 없는 글자면 빈 줄.</summary>
    public byte Row(char c, int y)
    {
        int i = c - First;
        if (i < 0 || i >= Count || y < 0 || y >= Height) return 0;
        return _rows[i * Height + y];
    }

    /// <summary>그 글자의 점이 켜져 있는지.</summary>
    public bool Dot(char c, int x, int y) => x is >= 0 and < Width && (Row(c, y) >> (7 - x) & 1) != 0;

    /// <summary>글줄의 너비(점).</summary>
    public static int Measure(string text) => text.Length * Width;

    /// <summary>
    /// 글자들을 늘어놓은 한 장. 색인은 <b>0 이 바탕, 1 이 글자</b>다 — 구워 둘 때 쓴다.
    /// </summary>
    public byte[] ToSheet(out int width, out int height)
    {
        int rows = (Count + SheetCols - 1) / SheetCols;
        width = SheetCols * Width;
        height = rows * Height;
        var px = new byte[width * height];

        for (int i = 0; i < Count; i++)
        {
            int ox = (i % SheetCols) * Width, oy = (i / SheetCols) * Height;
            for (int y = 0; y < Height; y++)
            {
                byte bits = _rows[i * Height + y];
                for (int x = 0; x < Width; x++)
                    px[(oy + y) * width + ox + x] = (byte)(bits >> (7 - x) & 1);
            }
        }
        return px;
    }

    /// <summary>구워 둔 한 장을 도로 글꼴로.</summary>
    public static GameFont FromSheet(byte[] px, int width)
    {
        var rows = new byte[Count * Height];
        for (int i = 0; i < Count; i++)
        {
            int ox = (i % SheetCols) * Width, oy = (i / SheetCols) * Height;
            for (int y = 0; y < Height; y++)
            {
                int bits = 0;
                for (int x = 0; x < Width; x++)
                    if (px[(oy + y) * width + ox + x] != 0) bits |= 1 << (7 - x);
                rows[i * Height + y] = (byte)bits;
            }
        }
        return new GameFont(rows);
    }
}
