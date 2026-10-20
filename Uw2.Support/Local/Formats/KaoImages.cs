namespace Uw2.Support.Local.Formats;

/// <summary>
/// <c>KAO.LZW</c> — 청크 256개. 앞 128개가 <b>얼굴</b>(64 x 80), 뒤 128개가 <b>발견물 그림</b>(48 x 48)이다.
/// </summary>
/// <remarks>
/// 얼굴을 찍는 손 <c>KOUKAI2.EXE 0x00434E70(x, y, 번호)</c> 가 청크를 읽어 <c>0x00401060(x, y, 8, 0x50, 담개)</c> 로 넘긴다 —
/// 가로 8바이트(64점) · 세로 80줄이다. 1,920 = 64 x 80 x 3 / 8 이라 <b>면 셋(8색)</b>이고, 줄마다
/// <b>8점 한 토막에 면 바이트 셋이 붙어 있다</b>.
/// <code>
///   바이트 자리 = 줄 x (가로/8 x 3) + 토막 x 3 + 면
///   점 x 의 비트 = 7 - x % 8          (MSB 먼저)
///   색인 = 면0 | 면1&lt;&lt;1 | 면2&lt;&lt;2       0~7, 팔레트 앞 여덟 칸을 그대로 쓴다
/// </code>
/// 864바이트 청크도 같은 꼴로 48 x 48 이다(864 = 48 x 48 x 3 / 8) — 새 · 판다 · 공룡 같은 발견물이 나온다.
/// 예전에 「겹판 · 꽉채움에 다 대 봐도 어긋난다」던 것은 면이 줄 단위가 아니라 <b>토막 단위</b>로 엇갈려 있어서였다.
/// </remarks>
public static class KaoImages
{
    public const int FaceW = 64, FaceH = 80, FaceBytes = FaceW * FaceH * 3 / 8;   // 1,920
    public const int FindW = 48, FindH = 48, FindBytes = FindW * FindH * 3 / 8;   // 864
    public const int FaceCount = 128, FindCount = 128;

    /// <summary>청크 하나를 색인 그림으로 푼다. 크기는 청크 길이로 가른다.</summary>
    public static (byte[] Pixels, int W, int H) Decode(byte[] chunk)
    {
        var (w, h) = chunk.Length switch
        {
            FaceBytes => (FaceW, FaceH),
            FindBytes => (FindW, FindH),
            _ => throw new InvalidDataException($"KAO 청크 길이가 낯섭니다 ({chunk.Length})"),
        };
        return (Decode(chunk, w, h), w, h);
    }

    /// <summary>면 셋을 토막마다 엇갈려 담은 그림을 푼다.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> data, int w, int h)
    {
        var px = new byte[w * h];
        int rowBytes = w / 8 * 3;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int at = y * rowBytes + x / 8 * 3, bit = 7 - x % 8, v = 0;
                for (int p = 0; p < 3; p++)
                    if (((data[at + p] >> bit) & 1) != 0) v |= 1 << p;
                px[y * w + x] = (byte)v;
            }
        return px;
    }

    /// <summary>같은 크기 그림들을 가로 <paramref name="cols"/> 장씩 깐다.</summary>
    public static byte[] Atlas(IReadOnlyList<byte[]> images, int w, int h, int cols, out int width, out int height)
    {
        int rows = (images.Count + cols - 1) / cols;
        width = cols * w; height = rows * h;
        var atlas = new byte[width * height];
        for (int i = 0; i < images.Count; i++)
        {
            int ox = i % cols * w, oy = i / cols * h;
            for (int y = 0; y < h; y++)
                Array.Copy(images[i], y * w, atlas, (oy + y) * width + ox, w);
        }
        return atlas;
    }
}
