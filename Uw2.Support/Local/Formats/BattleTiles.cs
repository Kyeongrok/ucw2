namespace Uw2.Support.Local.Formats;

/// <summary>
/// 해전 그림 <c>HDAT.PUT</c>(47,104 바이트) — 32 x 32 조각 아흔둘, 조각마다 512 바이트(한 바이트에 두 점, 높은 네 비트가 왼쪽).
/// <c>KOUKAI2.EXE 0x0040DC80</c> 이 통째로 읽고 판 칸은 <c>(칸 &amp; 0x1F) x 512</c>(<c>0x0040E0A5</c>), 배는 <c>0x1A00 + (뱃머리 + 6 갈래) x 0x400</c>
/// (<c>0x0040DE5C</c>)에서 그린다 — 가림(0 · 0xF 만, 0xF 인 점이 비친다) 다음 조각이 배다.
/// </summary>
/// <remarks>
/// <code>
///   0 · 1      물결            4 민뭍          5 ~ 12 해안
///   13 ~ 60    가림 · 배 짝 스물넷 — 갈래 0 제 돛배 · 1 남 돛배 · 2 제 노배 · 3 남 노배, 갈래마다 뱃머리 여섯
///   61 ~       물보라 · 고르개 · 방향 화살 · 불꽃
/// </code>
/// </remarks>
public sealed class BattleTiles
{
    public const int Size = 32, Bytes = Size * Size / 2, ShipFirst = 13;

    private readonly byte[] _raw;

    public BattleTiles(byte[] raw) => _raw = raw;

    public int Count => _raw.Length / Bytes;

    /// <summary>조각 하나의 색 번호(0 ~ 15) — 줄 먼저.</summary>
    public byte[] Indices(int tile)
    {
        var px = new byte[Size * Size];
        int o = tile * Bytes;
        for (int i = 0; i < Bytes && o + i < _raw.Length; i++)
        {
            byte b = _raw[o + i];
            px[i * 2] = (byte)(b >> 4);
            px[i * 2 + 1] = (byte)(b & 0x0F);
        }
        return px;
    }

    /// <summary>조각을 BGRA 로 — <paramref name="mask"/> 가 있으면 그 조각의 0xF 인 점을 비운다.</summary>
    public uint[] Bgra(int tile, GamePalette palette, int mask = -1)
    {
        var px = Indices(tile);
        var m = mask >= 0 ? Indices(mask) : null;
        var outp = new uint[px.Length];
        for (int i = 0; i < px.Length; i++)
            outp[i] = m != null && m[i] == 0x0F ? 0u : palette.Bgra[px[i] & 0x0F];
        return outp;
    }

    /// <summary>배의 가림 조각 번호(<c>0x0040DE47</c>) — 갈래 = (남 배면 1) + (노 젓는 배면 2). 배 그림은 이 다음 조각이다.</summary>
    public static int Ship(bool enemy, bool oars, int heading) => ShipFirst + 2 * (heading % 6 + 6 * ((enemy ? 1 : 0) + (oars ? 2 : 0)));
}
