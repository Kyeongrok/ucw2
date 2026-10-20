using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 교역 — 교역품 마흔여섯, 시장 열셋, 항구 형편 백. 교역소(<c>KOUKAI2.EXE 0x004216F0</c>)가 쓰는 표들이다.
/// </summary>
/// <remarks>
/// <code>
///   교역품 이름   KOUKAI2-han8.exe 파일 0x550F0, 11바이트 x 46 (일본어판 메모리 0x00456EF0)
///   갈래 경계     0x004570F0 = 06 0A 11 15 1A 1F 21 24 28 2E   (0x004202E0: 경계가 g 보다 큰 첫 차례)
///   시장          SDATA.DAT 0x605A, 128바이트 x 13   (메모리 0x0045FF88, 0x0043AF10)
///   항구 형편      SDATA.DAT 0x66DA, 37바이트 x 100   (메모리 0x0045F110, 0x0043AEF0)
/// </code>
/// 시장 한 벌(128바이트):
/// <code>
///   +0x00  낱말 x 46   이 시장이 교역품마다 쳐 주는 값(팔 때)
///   +0x5C  낱말 x 9    이 시장이 파는 교역품의 바탕 값(살 때)
///   +0x6E  바이트 x 9  파는 교역품(0xFF 는 빈 칸)
///   +0x77  바이트 x 9  그 교역품이 나오는 경제 — 항구 경제가 이것 x 10 이상이어야 판다(0x004204DF)
/// </code>
/// 항구 형편(37바이트): <c>+0</c> 경제 · <c>+4</c> 공업(낱말) · <c>+8</c> 나라 몫 여섯 · <c>+0x0E</c> 갈래 값 지수 열
/// (50 언저리) · <c>+0x18</c> 특산품 바탕 값(낱말) · <c>+0x1A</c> 특산품 · <c>+0x21</c> 시장 번호.
///
/// 사는 값(<c>0x004208D0</c>): <c>바탕 x (갈래 지수 + 50) / 100</c> 에 그 나라 허가증(소지품 <c>0x23 + 나라</c>)이 없으면
/// x 12/10 (<c>0x00420430</c>). 파는 값(<c>0x004210E0</c>): <c>쳐 주는 값 x (갈래 지수 + 50) / 100</c>, 그 항구 특산품이면
/// <c>특산품 바탕 x (지수 + 50) / 200</c>.
/// </remarks>
public sealed class Economy
{
    public const int GoodsCount = 46, NameSize = 11, NameOffsetHan8 = 0x550F0;
    public const int MarketOffset = 0x605A, MarketSize = 128, MarketCount = 13, MarketSlots = 9;
    public const int StateOffset = 0x66DA, StateSize = 37, StateCount = 100;

    /// <summary>갈래 경계 — <c>0x004570F0</c>.</summary>
    public static ReadOnlySpan<byte> CategoryEnds => [0x06, 0x0A, 0x11, 0x15, 0x1A, 0x1F, 0x21, 0x24, 0x28, 0x2E];

    /// <summary>갈래 이름(han8 파일 0x552F6 뒤).</summary>
    public static readonly string[] CategoryNames =
        ["향신료", "기호품", "식품", "섬유", "직물", "보석", "귀금속", "광석", "사치품", "기타"];

    public readonly record struct Market(int[] Pays, int[] BuyBase, int[] Goods, int[] Need);

    /// <param name="Yard">+0x22 조선소 갈래(<see cref="ShipyardTypes"/> 의 줄).</param>
    /// <param name="Building">+0x23 짓고 있는 배(0xFF 없음).</param>
    public readonly record struct PortState(int Economy, int Industry, int[] Shares, int[] PriceIndex,
                                            int SpecialBase, int Special, int Zone, int Yard = 0, int Building = -1);

    /// <summary>
    /// 조선소 갈래마다 지을 수 있는 선종 — <c>KOUKAI2.EXE 0x00458BD0</c>, 8바이트 x 11 (0xFF 빈 칸).
    /// 리스본 · 세빌리아는 0, 런던 1, 암스테르담 · 함부르크 2, 제노바 4, 이스탄불 5, 나가사키 9(관선 · 안택선 · 철갑선) …
    /// </summary>
    public static readonly int[][] ShipyardTypes =
    [
        [0, 18, 5, 7, 19, 8, 9, 10],
        [6, 8, 9, 10, 13, 14, 15],
        [1, 5, 8, 9, 10, 14, 16],
        [1, 18, 6, 19, 8, 12, 21, 10],
        [18, 4, 5, 3, 19, 8, 20, 9],
        [18, 5, 19, 8, 11, 20, 9],
        [18, 2, 11],
        [18, 6, 8, 9],
        [17],
        [24, 23, 22],
        [7, 12, 8, 9, 10],
    ];

    public IReadOnlyList<string> GoodsNames { get; }
    public IReadOnlyList<Market> Markets { get; }
    public IReadOnlyList<PortState> States { get; }

    /// <summary>구워 둘 때 쓰는 날바이트.</summary>
    public byte[] MarketBytes { get; }
    public byte[] StateBytes { get; }

    private Economy(IReadOnlyList<string> names, byte[] markets, byte[] states)
    {
        GoodsNames = names; MarketBytes = markets; StateBytes = states;
        Markets = [.. Enumerable.Range(0, markets.Length / MarketSize).Select(k => ParseMarket(markets, k * MarketSize))];
        States = [.. Enumerable.Range(0, states.Length / StateSize).Select(k => ParseState(states, k * StateSize))];
    }

    public static Economy FromParts(IReadOnlyList<string> names, byte[] markets, byte[] states) => new(names, markets, states);

    /// <summary><c>SDATA.DAT</c> 와 한글판 EXE(이름)에서 읽는다.</summary>
    public static Economy Load(string sdataPath, string han8ExePath)
    {
        var d = File.ReadAllBytes(sdataPath);
        var exe = File.ReadAllBytes(han8ExePath);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        var names = Enumerable.Range(0, GoodsCount)
            .Select(i => cp949.GetString(exe, NameOffsetHan8 + i * NameSize, NameSize).Split('\0')[0]).ToArray();
        return new Economy(names, d.AsSpan(MarketOffset, MarketSize * MarketCount).ToArray(),
                           d.AsSpan(StateOffset, StateSize * StateCount).ToArray());
    }

    private static Market ParseMarket(byte[] r, int o)
    {
        var pays = new int[GoodsCount];
        for (int i = 0; i < GoodsCount; i++) pays[i] = BitConverter.ToUInt16(r, o + i * 2);
        var buy = new int[MarketSlots]; var goods = new int[MarketSlots]; var need = new int[MarketSlots];
        for (int i = 0; i < MarketSlots; i++)
        {
            buy[i] = BitConverter.ToUInt16(r, o + 0x5C + i * 2);
            goods[i] = r[o + 0x6E + i] == 0xFF ? -1 : r[o + 0x6E + i];
            need[i] = r[o + 0x77 + i];
        }
        return new Market(pays, buy, goods, need);
    }

    private static PortState ParseState(byte[] r, int o) => new(
        BitConverter.ToUInt16(r, o), BitConverter.ToUInt16(r, o + 4),
        [.. r.AsSpan(o + 8, 6).ToArray().Select(b => (int)b)],
        [.. r.AsSpan(o + 0x0E, 10).ToArray().Select(b => (int)b)],
        BitConverter.ToUInt16(r, o + 0x18), r[o + 0x1A] == 0xFF ? -1 : r[o + 0x1A], r[o + 0x21],
        r[o + 0x22], r[o + 0x23] == 0xFF ? -1 : r[o + 0x23]);

    /// <summary>
    /// 그 항구 조선소가 지을 수 있는 선종 — 갈래 줄 가운데 공업이 <c>선종 +0 x 10</c> 이상인 것
    /// (<c>0x00428958</c> 이 첫 선종으로 「못 짓는다」(말 0xFB)를 가른다 — 우리는 줄마다 같은 셈으로 거른다).
    /// </summary>
    public List<int> Buildable(int port, IReadOnlyList<StartData.ShipType> types)
    {
        var list = new List<int>();
        if (port < 0 || port >= States.Count) return list;
        var s = States[port];
        if (s.Yard < 0 || s.Yard >= ShipyardTypes.Length) return list;
        foreach (int t in ShipyardTypes[s.Yard])
            if (t < types.Count && types[t].Field0 * 10 <= s.Industry) list.Add(t);
        return list;
    }

    /// <summary>교역품의 갈래 — <c>0x004202E0</c>(경계를 넘는 값은 0 갈래로 떨어진다).</summary>
    public static int CategoryOf(int good)
    {
        for (int i = 0; i < CategoryEnds.Length; i++) if (CategoryEnds[i] > good) return i;
        return 0;
    }

    /// <summary>그 항구 교역소가 파는 것 — (교역품, 값). 특산품이 있으면 맨 뒤에 붙는다.</summary>
    public List<(int Good, int Price)> ForSale(int port, bool permit = false)
    {
        var list = new List<(int, int)>();
        if (port < 0 || port >= States.Count) return list;
        var s = States[port];
        if (s.Zone >= Markets.Count) return list;
        var m = Markets[s.Zone];
        for (int i = 0; i < MarketSlots; i++)
        {
            int g = m.Goods[i];
            if (g < 0) break;
            if (m.Need[i] * 10 > s.Economy) continue;
            list.Add((g, Price(m.BuyBase[i], s.PriceIndex[CategoryOf(g)], permit)));
        }
        if (s.Special >= 0 && s.Special < GoodsCount)
            list.Add((s.Special, Price(s.SpecialBase, s.PriceIndex[CategoryOf(s.Special)], permit)));
        return list;
    }

    /// <summary>
    /// 그 항구 교역소가 쳐 주는 값 — <c>0x004210E0</c>. 그 항구 특산품이면 바탕의 절반 셈(<c>/200</c>)이다.
    /// </summary>
    public int SellPrice(int port, int good)
    {
        if (port < 0 || port >= States.Count || good < 0 || good >= GoodsCount) return 0;
        var s = States[port];
        int index = s.PriceIndex[CategoryOf(good)] + 50;
        if (good == s.Special) return s.SpecialBase * index / 200;
        return s.Zone < Markets.Count ? Markets[s.Zone].Pays[good] * index / 100 : 0;
    }

    private static int Price(int baseValue, int index, bool permit) =>
        baseValue * (index + 50) / 100 * (permit ? 10 : 12) / 10;
}
