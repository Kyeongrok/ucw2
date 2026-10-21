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

    /// <summary>조각 얼굴 벌의 자리(<c>KAO.LZW</c> 파일 안, LS 청크 뒤)와 벌 하나의 크기 — 나라마다 한 벌, 일곱 벌.</summary>
    public const int PartsAt = 0x476A2, PartSetBytes = 0x60C0, PartSets = 7;

    /// <summary>
    /// 조각 얼굴(<c>0x00434CC0</c>) — 사람 +0x1A 낱말의 위 두 비트(0xC000)가 서면 얼굴 번호가 아니라 조각 고르기다.
    /// 벌(나라, 사람 +0x31 &amp; 0xF)에서 다섯 조각을 골라 1,920 바이트 얼굴(<see cref="FaceBytes"/>)을 짓는다.
    /// <code>
    ///   위(머리)   w &amp; 7         0x318 바이트를 0 에        (벌 +0)        옮긴다
    ///   아래       (w &gt;&gt; 3) &amp; 7  0x468 바이트를 0x318 에    (벌 +0x18C0)   옮긴다
    ///   눈         (w &gt;&gt; 8) &amp; 7  0x1B0 바이트를 0x1F8 에    (벌 +0x3C00)   겹친다(OR)
    ///   코         (w &gt;&gt; 6) &amp; 3  0x240 바이트를 0x2B8 에    (벌 +0x4980)   겹친다
    ///   입         (w &gt;&gt; 11) &amp; 7 0x1C8 바이트를 0x438 에    (벌 +0x5280)   겹친다
    /// </code>
    /// <paramref name="parts"/> 는 일곱 벌을 이어 붙인 것이다.
    /// </summary>
    public static byte[] Compose(byte[] parts, int set, int word)
    {
        var face = new byte[FaceBytes];
        int b = Math.Clamp(set, 0, PartSets - 1) * PartSetBytes;
        void Part(int at, int size, int dst, int index, bool or)
        {
            int src = b + at + index * size;
            if (src + size > parts.Length) return;
            for (int i = 0; i < size; i++)
                face[dst + i] = or ? (byte)(face[dst + i] | parts[src + i]) : parts[src + i];
        }
        Part(0, 0x318, 0, word & 7, false);
        Part(0x18C0, 0x468, 0x318, word >> 3 & 7, false);
        Part(0x3C00, 0x1B0, 0x1F8, word >> 8 & 7, true);
        Part(0x4980, 0x240, 0x2B8, word >> 6 & 3, true);
        Part(0x5280, 0x1C8, 0x438, word >> 11 & 7, true);
        return face;
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
