using System.Buffers.Binary;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// <c>NPK016</c> 그림 — <c>GRAPH.DAT</c> · <c>END.DAT</c> · <c>OPEN.DAT</c> 에 여러 장이 이어 붙어 있다.
/// </summary>
/// <remarks>
/// 머리 0x30 바이트:
/// <code>
///   +0x00  "NPK016"
///   +0x06  면 수(4)          +0x08  화면 640 x 400
///   +0x0C  그림 가로 · 세로(워드)
///   +0x10  빛깔 열여섯 — 워드마다 (G&lt;&lt;8) | (R&lt;&lt;4) | B, 각 0~15. 기본 빛깔일 뿐 원본 그림 손(<c>0x0043AC40</c>)은
///          이것을 쓰지 않고 그때 화면 빛깔(바다 화면)로 찍는다 — 굽는 쪽도 화면 빛깔을 쓴다
/// </code>
/// 몸은 <c>0x0044D961</c> 이 푼다(그림 찍는 손 <c>0x0043AC40</c> 이 화면 꼴 3 에서 부른다). 네 점이 한 토막이고, 줄마다
/// (가로 + 3) / 4 토막이다. 표지 바이트의 비트를 아래부터 하나씩 쓴다.
/// <code>
///   비트 0 — 날 토막: 바이트 둘 (a, b). 점 넷이 왼쪽부터 각 바이트의 비트 7 · 3 에서 나온다.
///            색 = a.7 x 8 + a.3 x 4 + b.7 x 2 + b.3, 그 뒤 a · b 를 한 칸씩 왼쪽으로.
///   비트 1 — 되풀이: 바이트 하나 c. (c &amp; 0x1F) + 1 토막을 (c &amp; 0x60) / 8 + 4 바이트 앞에서 베낀다.
///            c 의 비트 7 이 서면 거리는 그만큼의 「줄」(줄 너비 / 4 x 거리 = 1 ~ 4 줄 위)이다.
/// </code>
/// 원본은 다섯 줄 고리 버퍼에 풀어 한 줄씩 화면에 옮긴다. 여기서는 통째로 푼다.
/// 실행 파일의 자리 표(<c>0x004578A0</c>)는 이 <c>GRAPH.DAT</c> 와 어긋나서, 머리 글자를 찾아 가른다.
/// </remarks>
public static class Npk
{
    public const int HeaderSize = 0x30;
    private static readonly byte[] Magic = "NPK016"u8.ToArray();

    /// <summary>풀린 그림 — 점은 색인 0~15(줄 먼저), 빛깔은 RGB 세 바이트씩 열여섯.</summary>
    public readonly record struct Picture(int Width, int Height, byte[] Pixels, byte[] Palette);

    /// <summary>파일 안 그림마다 시작 자리.</summary>
    public static List<int> Offsets(ReadOnlySpan<byte> file)
    {
        var at = new List<int>();
        for (int i = 0; i + Magic.Length <= file.Length;)
        {
            int k = file[i..].IndexOf(Magic);
            if (k < 0) break;
            at.Add(i + k);
            i += k + Magic.Length;
        }
        return at;
    }

    /// <summary>파일 안 그림을 모두 푼다.</summary>
    public static List<Picture> ReadAll(byte[] file)
    {
        var offs = Offsets(file);
        var list = new List<Picture>(offs.Count);
        for (int i = 0; i < offs.Count; i++)
        {
            int end = i + 1 < offs.Count ? offs[i + 1] : file.Length;
            list.Add(Decode(file.AsSpan(offs[i], end - offs[i])));
        }
        return list;
    }

    /// <summary>머리부터 시작하는 그림 한 장을 푼다.</summary>
    public static Picture Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("NPK016 이 아닙니다");
        int w = BinaryPrimitives.ReadUInt16LittleEndian(data[0x0C..]);
        int h = BinaryPrimitives.ReadUInt16LittleEndian(data[0x0E..]);
        var pal = new byte[16 * 3];
        for (int k = 0; k < 16; k++)
        {
            int v = BinaryPrimitives.ReadUInt16LittleEndian(data[(0x10 + 2 * k)..]);
            pal[k * 3] = (byte)((v >> 4 & 15) * 17);
            pal[k * 3 + 1] = (byte)((v >> 8 & 15) * 17);
            pal[k * 3 + 2] = (byte)((v & 15) * 17);
        }
        var rows = Unpack(data[HeaderSize..], w, h);
        int stride = (w + 3) / 4 * 4;
        var px = new byte[w * h];
        for (int y = 0; y < h; y++) rows.AsSpan(y * stride, w).CopyTo(px.AsSpan(y * w));
        return new Picture(w, h, px, pal);
    }

    /// <summary>
    /// 빛깔 없는 꼴 — <c>END.DAT</c> · <c>OPEN.DAT</c>. 머리는 12 바이트(<c>NPK016</c>, 가로 +6, 세로 +8, 머리까지 친 길이 +0x0A)이고
    /// 몸은 같은 꼴이다(<c>0x0042E060</c> → <c>0x00449FD1</c>). 빛깔은 실행 파일에 따로 있다(<see cref="ExePalette"/>).
    /// </summary>
    public static Picture DecodeBare(ReadOnlySpan<byte> data, byte[] palette)
    {
        if (data.Length < 12 || !data[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("NPK016 이 아닙니다");
        int w = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        int h = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        var rows = Unpack(data[12..], w, h);
        int stride = (w + 3) / 4 * 4;
        var px = new byte[w * h];
        for (int y = 0; y < h; y++) rows.AsSpan(y * stride, w).CopyTo(px.AsSpan(y * w));
        return new Picture(w, h, px, palette);
    }

    /// <summary>
    /// 실행 파일의 빛깔 48 바이트(<c>0x0042E1E0</c> 이 받는 것) — 칸마다 (B, R, G) 순 0~15 다. RGB 로 바꾼다.
    /// </summary>
    public static byte[] ExePalette(ReadOnlySpan<byte> brg)
    {
        var pal = new byte[16 * 3];
        for (int k = 0; k < 16; k++)
        {
            pal[k * 3] = (byte)((brg[k * 3 + 1] & 15) * 17);
            pal[k * 3 + 1] = (byte)((brg[k * 3 + 2] & 15) * 17);
            pal[k * 3 + 2] = (byte)((brg[k * 3] & 15) * 17);
        }
        return pal;
    }

    /// <summary>
    /// 주인공(사람 표 차례 0 ~ 5)마다 엔딩 그림 — <c>END.DAT</c> 의 자리와 빛깔 자리(VA). <c>0x00405B50</c> 의 갈래표다.
    /// </summary>
    public static readonly (int Offset, int PaletteVa)[] HeroEndings =
    [
        (0x2C7F0, 0x4545C0),   // 조안 — 누워 책을 읽는다
        (0x05DF1, 0x4545F0),   // 카탈리나 — 깃펜을 문다
        (0x00000, 0x456D00),   // 옷토 — 뒷짐을 지고 기댄다
        (0x1C086, 0x456D00),   // 에르네스트 — 차를 내오는 이와 함께
        (0x280B6, 0x454620),   // 피에트로 — 책상에 엎드린다
        (0x0F50D, 0x456D00),   // 알 — 둘이서 책을 본다
    ];

    /// <summary>실행 파일 <c>.data</c> 의 VA 를 파일 자리로(<c>KOUKAI2.EXE</c> — 0x454000 이 0x52200 에 놓인다).</summary>
    public static int DataFileOffset(int va) => va - 0x454000 + 0x52200;

    /// <summary>몸을 푼다 — 줄 너비는 4 의 배수로 올린 것이다. 모자라는 바이트는 0 으로 친다.</summary>
    public static byte[] Unpack(ReadOnlySpan<byte> src, int w, int h)
    {
        int units = (w + 3) / 4, stride = units * 4;
        var o = new byte[stride * h];
        int i = 0, flags = 0;
        byte Next(ReadOnlySpan<byte> s) => i < s.Length ? s[i++] : (byte)0;
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < units;)
            {
                if ((flags & 0xFF00) == 0) flags = Next(src) | 0xFF00;
                int c = Next(src);
                if ((flags & 1) == 0)
                {
                    int b = Next(src);
                    for (int k = 0; k < 4; k++)
                    {
                        o[row + x * 4 + k] = (byte)((c >> 7 & 1) * 8 + (c >> 3 & 1) * 4 + (b >> 7 & 1) * 2 + (b >> 3 & 1));
                        c = c << 1 & 0xFF; b = b << 1 & 0xFF;
                    }
                    x++;
                }
                else
                {
                    int n = (c & 0x1F) + 1, dist = ((c & 0x60) >> 3) + 4;
                    if ((c & 0x80) != 0) dist = stride / 4 * dist;
                    int to = row + x * 4, from = to - dist;
                    for (int k = 0; k < n * 4 && to + k < o.Length; k++)
                        o[to + k] = from + k >= 0 ? o[from + k] : (byte)0;
                    x += n;
                }
                flags >>= 1;
            }
        }
        return o;
    }
}
