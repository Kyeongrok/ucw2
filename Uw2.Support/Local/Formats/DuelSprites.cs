namespace Uw2.Support.Local.Formats;

/// <summary>
/// 일기토 그림 — <c>IAP1 ~ 6.LZW</c>(주인공 + 1, 왼쪽을 본다) · <c>IAE1.LZW</c>(상대, 오른쪽을 본다). LS 묶음, 청크 서른다섯.
/// </summary>
/// <remarks>
/// 일기토 채비(<c>0x00414955</c>)가 둘을 연다. 청크는 다섯씩 일곱 무리 — 무리 g 의 청크 5g 가 몸, 5g + 1 ~ 5g + 4 가 무기 겹그림
/// 넷(무기 갈래 0 ~ 3)이다. 무리는 0 서기 · 맞음 · 쓰러짐 · 이김, 1 찌르기, 2 베기, 3 강타, 4 쳐내기, 5 받기, 6 피하기이고
/// 장 수는 [6, 3, 4, 3, 3, 3, 3] 이다. 청크 앞은 조각 표 — 8 바이트씩
/// <code>
///   x(8 점 단위) · y · 가로(바이트) · 세로 · 0 · 0 · 자리(빅엔디언 낱말)        세로 0xFF 로 끝
/// </code>
/// 조각 몸은 줄마다 8 점 토막을 왼쪽부터, 토막마다 면 바이트 넷(면 0 이 낮은 비트, MSB 가 왼쪽 점). 색 8 은 비침이다.
/// 장 f 는 몸 조각 f 위에 무기 조각 f 를 얹는다.
/// </remarks>
public static class DuelSprites
{
    public const int Groups = 7, Variants = 4, Clear = 8, CellW = 176, CellH = 120, MaxFrames = 6;
    public static readonly int[] Frames = [6, 3, 4, 3, 3, 3, 3];

    public readonly record struct Part(int X, int Y, int WidthBytes, int Height, int Offset);

    public static List<Part> Parts(byte[] chunk)
    {
        var list = new List<Part>();
        for (int i = 0; i + 8 <= chunk.Length; i += 8)
        {
            int h = chunk[i + 3], w = chunk[i + 2];
            if (h == 0xFF || w == 0) break;
            list.Add(new Part(chunk[i], chunk[i + 1], w, h, chunk[i + 6] << 8 | chunk[i + 7]));
        }
        return list;
    }

    /// <summary>조각 하나를 <paramref name="cell"/>(가로 <see cref="CellW"/>)에 얹는다 — 비침 색은 건너뛴다.</summary>
    public static void Blit(byte[] chunk, Part p, byte[] cell, int cellW)
    {
        for (int r = 0; r < p.Height; r++)
            for (int bx = 0; bx < p.WidthBytes; bx++)
            {
                int o = p.Offset + (r * p.WidthBytes + bx) * 4;
                if (o + 3 >= chunk.Length) return;
                for (int bit = 0; bit < 8; bit++)
                {
                    int v = 0;
                    for (int pl = 0; pl < 4; pl++) v |= (chunk[o + pl] >> (7 - bit) & 1) << pl;
                    int x = (p.X + bx) * 8 + bit, y = p.Y + r;
                    if (v != Clear && x < cellW && y * cellW + x < cell.Length) cell[y * cellW + x] = (byte)v;
                }
            }
    }

    /// <summary>
    /// 한 벌(무기 갈래 <paramref name="variant"/>)을 판 하나로 — 줄이 무리, 칸이 장(<see cref="CellW"/> x <see cref="CellH"/>).
    /// 빈 곳은 비침 색이다.
    /// </summary>
    public static byte[] Sheet(IReadOnlyList<byte[]> chunks, int variant, out int width, out int height)
    {
        width = CellW * MaxFrames; height = CellH * Groups;
        var sheet = new byte[width * height];
        Array.Fill(sheet, (byte)Clear);
        var cell = new byte[CellW * CellH];
        for (int g = 0; g < Groups && 5 * g + 1 + variant < chunks.Count; g++)
        {
            var body = Parts(chunks[5 * g]);
            var arms = Parts(chunks[5 * g + 1 + variant]);
            for (int f = 0; f < Math.Min(body.Count, MaxFrames); f++)
            {
                Array.Fill(cell, (byte)Clear);
                Blit(chunks[5 * g], body[f], cell, CellW);
                if (f < arms.Count) Blit(chunks[5 * g + 1 + variant], arms[f], cell, CellW);
                for (int y = 0; y < CellH; y++)
                    cell.AsSpan(y * CellW, CellW).CopyTo(sheet.AsSpan((g * CellH + y) * width + f * CellW));
            }
        }
        return sheet;
    }
}
