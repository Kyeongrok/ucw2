namespace Uw2.Support.Local.Formats;

/// <summary>
/// 칸 값을 <b>2 x 2 칩</b>으로 편다. 화면에 실제로 깔리는 것은 이쪽이다.
/// </summary>
/// <remarks>
/// 세계지도 한 칸(1080 x 540)은 그리는 자리에서 <b>칩 넷</b>이 된다 — 곧 온 지도가
/// <b>2160 x 1080 칩</b>이다. <c>MONSTER.DAT</c> 의 좌표가 칸의 두 배였던 것이 이것이고,
/// 세이브의 함대 자리도 이 눈금일 것이다.
///
/// 게임의 <c>KOUKAI2.EXE 0x0041DE78</c> 을 그대로 옮겼다.
/// <code>
///   값 &lt; 16  : 네 비트가 그대로 2x2 뭍 자국이다
///               비트3 좌상 · 비트2 우상 · 비트1 좌하 · 비트0 우하
///               켜져 있으면 뭍 칩(0x41), 꺼져 있으면 바다 칩(0)
///   값 &gt;= 16 : 그림표에서 칩 넷을 꺼낸다(DATA1.LZW 18번 청크, 256칸 x 4바이트)
///               꺼낸 칩이 0x34 보다 작으면 바다(0) 로 눌린다
/// </code>
/// 그래서 <b>바다는 칩 0, 민뭍은 칩 0x41(65)</b> 이다. 해안 칸(1~14)은 네 귀 가운데
/// 몇 개만 뭍인 자국이고, 16 이상은 강·산·숲·사막·얼음처럼 그림표가 따로 있는 것들이다.
/// </remarks>
public sealed class ChipMap
{
    /// <summary>칸 하나가 차지하는 칩 수(한 변).</summary>
    public const int PerCell = 2;

    /// <summary>칩으로 잰 지도 크기.</summary>
    public const int Width = WorldMap.Width * PerCell;    // 2160
    public const int Height = WorldMap.Height * PerCell;  // 1080

    /// <summary>바다 칩.</summary>
    public const byte SeaChip = 0;

    /// <summary>민뭍 칩. 값 &lt; 16 의 켜진 자리에 이것이 들어간다.</summary>
    public const byte LandChip = 0x41;

    /// <summary>이보다 작은 칩 번호는 바다로 눌린다(<c>0x0041DEA3</c> 의 <c>cmp al, 0x34</c>).</summary>
    public const byte MinChip = 0x34;

    /// <summary>그림표 칸 수.</summary>
    public const int QuadCount = 256;

    private readonly byte[] _quads;   // 256 x 4

    private ChipMap(byte[] quads) => _quads = quads;

    /// <summary><c>DATA1.LZW</c> 의 1,024바이트 청크(18번)를 그림표로 읽는다.</summary>
    public static ChipMap FromQuadTable(ReadOnlySpan<byte> chunk1024)
    {
        if (chunk1024.Length != QuadCount * 4)
            throw new InvalidDataException($"그림표가 {QuadCount * 4}바이트가 아닙니다 ({chunk1024.Length})");
        return new ChipMap(chunk1024.ToArray());
    }

    /// <summary><c>DATA1.LZW</c> 에서 그림표 청크를 찾아 읽는다.</summary>
    public static ChipMap FromData1(string path)
    {
        foreach (var c in LsArchive.ReadAll(path))
            if (c.Length == QuadCount * 4) return FromQuadTable(c);
        throw new InvalidDataException("DATA1.LZW 에 1,024바이트 그림표가 없습니다");
    }

    /// <summary>칸 값 하나를 칩 넷으로. 차례는 <b>좌상 · 우상 · 좌하 · 우하</b>다.</summary>
    public (byte LT, byte RT, byte LB, byte RB) Quad(byte value)
    {
        if (value < 16)
            return ((value & 8) != 0 ? LandChip : SeaChip,
                    (value & 4) != 0 ? LandChip : SeaChip,
                    (value & 2) != 0 ? LandChip : SeaChip,
                    (value & 1) != 0 ? LandChip : SeaChip);

        // 게임이 짚는 차례가 0 → 좌상, 2 → 좌하, 1 → 우상, 3 → 우하 다(0x0041DEA0~0x0041DEED).
        int at = value * 4;
        return (Clamp(_quads[at]), Clamp(_quads[at + 1]), Clamp(_quads[at + 2]), Clamp(_quads[at + 3]));
    }

    private static byte Clamp(byte chip) => chip < MinChip ? SeaChip : chip;

    /// <summary>
    /// 배가 다닐 수 있는 칩인지. <b>칩 0(큰바다)만 참</b>이다.
    /// </summary>
    /// <remarks>
    /// EXE 안의 판정 자리는 아직 못 찾았고, 이것은 자료로 굳힌 것이다 —
    /// <c>MONSTER.DAT</c> 의 자리 서른 곳이 <b>하나도 빠짐없이 칩 0</b> 위에 있고,
    /// 그 둘레 3x3 이백일흔 칸 가운데 뭍 칩은 한 칸뿐이다.
    ///
    /// <b>해안 칩(52~63 따위)을 지날 수 있는지는 아직 안 갈렸다.</b> 3편에서도 이 자리를
    /// 오래 틀렸으니(볼트 <c>25.분석-칸 통행 판정</c>) 섣불리 넓히지 말 것.
    /// </remarks>
    public static bool CanSail(byte chip) => chip == SeaChip;

    /// <summary>온 지도를 칩 배열(2160 x 1080)로 편다. 셰이더에 그대로 올린다.</summary>
    public byte[] Expand(WorldMap map)
    {
        var chips = new byte[Width * Height];
        for (int y = 0; y < WorldMap.Height; y++)
            for (int x = 0; x < WorldMap.Width; x++)
            {
                var (lt, rt, lb, rb) = Quad(map[x, y]);
                int top = (y * 2) * Width + x * 2;
                chips[top] = lt;
                chips[top + 1] = rt;
                chips[top + Width] = lb;
                chips[top + Width + 1] = rb;
            }
        return chips;
    }
}
