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
    /// <param name="SpecialNeed">+0x1B 특산품이 나오는 경제(항구 경제 ≥ 이것 x 10).</param>
    public readonly record struct PortState(int Economy, int Industry, int[] Shares, int[] PriceIndex,
                                            int SpecialBase, int Special, int Zone, int Yard = 0, int Building = -1,
                                            int SpecialNeed = 0);

    /// <summary>특산품이 목록에 붙는 갈래 지수의 끝 — 이 아래여야 판다(<c>0x004204A4</c> 의 <c>cmp 0x5A</c>).</summary>
    public const int SpecialIndexLimit = 90;

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
    public IReadOnlyList<PortState> States { get; private set; }

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

    /// <summary>항구 형편 날바이트를 되돌린다 — 세이브를 불러올 때.</summary>
    public void RestoreStates(byte[] states)
    {
        states.AsSpan(0, Math.Min(states.Length, StateBytes.Length)).CopyTo(StateBytes);
        States = [.. Enumerable.Range(0, StateBytes.Length / StateSize).Select(k => ParseState(StateBytes, k * StateSize))];
    }

    /// <summary>날바이트를 떠 온 사본 — 게임마다 따로 바뀌게.</summary>
    public Economy Clone() => new(GoodsNames, (byte[])MarketBytes.Clone(), (byte[])StateBytes.Clone());

    public const int EconomyCap = 1000, InvestDivisor = 300, InvestCap = 50000, NoNation = 6;

    /// <summary>
    /// 그 항구에 넣을 수 있는 투자 — min(돈, 50000, 50000 − 이달 투자)(<c>0x004212B0</c> 교역소 · <c>0x0042A850</c> 조선소 공업).
    /// </summary>
    public int MaxInvest(int port, bool industry, int gold)
    {
        int at = port * StateSize + (industry ? 6 : 2);
        if (at + 2 > StateBytes.Length) return 0;
        return Math.Max(0, Math.Min(Math.Min(gold, InvestCap), InvestCap - BitConverter.ToUInt16(StateBytes, at)));
    }

    /// <summary>그 항구 조선소에서 짓는 배가 다 되기까지 남은 날 — 형편 +0x23(0xFF 면 짓는 배 없음 → -1).</summary>
    public int BuildDays(int port)
    {
        int o = port * StateSize + 0x23;
        return port < 0 || o >= StateBytes.Length || StateBytes[o] == 0xFF ? -1 : StateBytes[o];
    }

    public void SetBuildDays(int port, int days)
    {
        int o = port * StateSize + 0x23;
        if (port < 0 || o >= StateBytes.Length) return;
        StateBytes[o] = (byte)(days < 0 ? 0xFF : Math.Min(days, 0xFE));
        States = [.. Enumerable.Range(0, StateBytes.Length / StateSize).Select(k => ParseState(StateBytes, k * StateSize))];
    }

    /// <summary>투자 뒤의 항구 편(<c>0x0042A77C</c>) — 몫이 75 이상인 나라, 없으면 <see cref="NoNation"/>.</summary>
    public int Allegiance(int port)
    {
        int o = port * StateSize;
        if (o + StateSize > StateBytes.Length) return NoNation;
        int side = NoNation;
        for (int k = 0; k < 6; k++)
            if (StateBytes[o + 8 + k] >= 75) side = k;
        return side;
    }

    /// <summary>
    /// 투자한다 — 이달 투자(+2 · +6)에 더하고, 나라 몫을 올린다(<c>0x004213A0</c> · <c>0x0042A5D0</c>).
    /// 오르는 몫은 min(투자 / 경제(공업이면 공업), 100 − 그 나라 몫). 다른 나라 가운데 몫이 있는 것들이 그것을 똑같이 나눠 내고,
    /// 모자란 나라가 못 낸 것은 몫이 남은 나라가 하나씩 더 낸다. 주인공에 나라가 없으면(6) 올라가는 나라 없이 깎이기만 한다.
    /// 오른 몫을 돌려준다.
    /// </summary>
    public int Invest(int port, bool industry, int amount, int nation)
    {
        var r = StateBytes;
        int o = port * StateSize;
        if (amount <= 0 || o + StateSize > r.Length) return 0;
        int at = o + (industry ? 6 : 2);
        int v = Math.Min(0xFFFF, BitConverter.ToUInt16(r, at) + amount);
        r[at] = (byte)v; r[at + 1] = (byte)(v >> 8);

        int divisor = Math.Max(1, (int)BitConverter.ToUInt16(r, o + (industry ? 4 : 0)));
        int gain = nation == NoNation ? Math.Min(100, amount / divisor)
                                      : Math.Min(amount / divisor, 100 - r[o + 8 + nation]);
        gain = Math.Max(0, gain);
        if (nation != NoNation) r[o + 8 + nation] = (byte)(r[o + 8 + nation] + gain);
        int others = Enumerable.Range(0, 6).Count(k => k != nation && r[o + 8 + k] > 0);
        if (others > 0)
        {
            int each = gain / others, owed = 0;
            for (int k = 0; k < 6; k++)
            {
                if (k == nation || r[o + 8 + k] == 0) continue;
                if (r[o + 8 + k] >= each) r[o + 8 + k] -= (byte)each;
                else { owed += each - r[o + 8 + k]; r[o + 8 + k] = 0; }
            }
            while (owed > 0)
            {
                int before = owed;
                for (int k = 0; k < 6 && owed > 0; k++)
                    if (k != nation && r[o + 8 + k] > 0) { r[o + 8 + k]--; owed--; }
                if (owed == before) break;
            }
        }
        States = [.. Enumerable.Range(0, r.Length / StateSize).Select(k => ParseState(r, k * StateSize))];
        return gain;
    }

    /// <summary>
    /// 남 함대가 짐을 판다(<c>0x004021A2</c>) — 그 항구 값 지수 열 저마다 −min(지수, rand(3)), 판 짐의 갈래는 2 를 더 뺀다.
    /// </summary>
    public void NpcSell(int port, int good, Func<int, int> rand)
    {
        var r = StateBytes;
        int o = port * StateSize;
        if (port < 0 || o + StateSize > r.Length) return;
        int cat = CategoryOf(good);
        for (int i = 0; i < 10; i++)
        {
            int idx = r[o + 0x0E + i];
            r[o + 0x0E + i] = (byte)(idx - Math.Min(idx, rand(3) + (i == cat ? 2 : 0)));
        }
        States = [.. Enumerable.Range(0, r.Length / StateSize).Select(k => ParseState(r, k * StateSize))];
    }

    /// <summary>
    /// 주인공이 교역품을 사고판 뒤 값 지수 — 구입(<c>0x00420ACA</c>)은 올리고 매각(<c>0x00420F07</c>)은 내린다.
    /// 판 돈 <paramref name="total"/>(값 x 양)으로 그 갈래를 min(10, 판 돈 / (경제 + 500)), 열 갈래 모두를 min(3, 판 돈 / 1000) 만큼
    /// 옮기고 0 ~ 100 에서 자른다. 매각은 원본처럼 짐 칸 하나마다 부른다.
    /// </summary>
    public void PlayerTraded(int port, int good, int total, bool bought)
    {
        var r = StateBytes;
        int o = port * StateSize;
        if (port < 0 || o + StateSize > r.Length || good < 0 || good >= GoodsCount || total <= 0) return;
        int cat = CategoryOf(good);
        int economy = BitConverter.ToUInt16(r, o);
        int one = Math.Min(10, total / (economy + 500)), all = Math.Min(3, total / 1000);
        int Move(int idx, int d) => bought ? Math.Min(100, idx + d) : idx - Math.Min(idx, d);
        r[o + 0x0E + cat] = (byte)Move(r[o + 0x0E + cat], one);
        for (int i = 0; i < 10; i++) r[o + 0x0E + i] = (byte)Move(r[o + 0x0E + i], all);
        States = [.. Enumerable.Range(0, r.Length / StateSize).Select(k => ParseState(r, k * StateSize))];
    }

    /// <summary>
    /// 남 함대가 투자한다(<c>0x00402587</c>) — 돈 / 2 x 100 을 경제 투자(+2, 경제 &lt; 공업일 때 — 가득하면 공업) 또는 공업 투자(+6)에 넣고
    /// 돈은 반을 쓴다. 나라 몫은 min(남은 돈 x 6 / 10, 100 − 몫) 오르고, 다른 나라 몫에서(그 합까지) 하나씩 돌아가며 뺀다. 남은 돈을 돌려준다.
    /// </summary>
    public int NpcInvest(int port, int nation, int gold)
    {
        var r = StateBytes;
        int o = port * StateSize;
        if (port < 0 || o + StateSize > r.Length || nation is < 0 or >= 6) return gold;
        int Word(int f) => r[o + f] | r[o + f + 1] << 8;
        void Put(int f, int v) { r[o + f] = (byte)v; r[o + f + 1] = (byte)(v >> 8); }
        int add = gold / 2 * 100;
        int first = Word(0) < Word(4) ? 2 : 6, second = first == 2 ? 6 : 2;
        int at = Word(first) != InvestCap ? first : Word(second) != InvestCap ? second : -1;
        if (at >= 0) Put(at, Math.Min(InvestCap, Word(at) + add));
        gold -= gold / 2;
        int others = 0;
        for (int k = 0; k < 6; k++) if (k != nation) others += r[o + 8 + k];
        int own = r[o + 8 + nation];
        int gain = Math.Min(gold * 6 / 10, 100 - own);
        r[o + 8 + nation] = (byte)(own + gain);
        int take = Math.Min(gain, others);
        while (take > 0)
            for (int k = 0; k < 6 && take > 0; k++)
                if (k != nation && r[o + 8 + k] > 0) { r[o + 8 + k]--; take--; }
        States = [.. Enumerable.Range(0, r.Length / StateSize).Select(k => ParseState(r, k * StateSize))];
        return gold;
    }

    /// <summary>
    /// 남 함대가 사들인다(<c>0x00402798</c>) — 교역품 <paramref name="good"/>(원본은 rand(16) + 10)의 값 = (갈래 지수 + 50) x 그 시장이 쳐 주는 값 / 100.
    /// 돈 x 100 이 값보다 적으면 못 산다. 양 = min(1000, 돈 x 100 / 값), 그 갈래 지수 +min(100 − 지수, rand(3) + 3), 돈 −값 x 양 / 100.
    /// </summary>
    public (int Amount, int Cost)? NpcBuy(int port, int good, int gold, Func<int, int> rand)
    {
        var r = StateBytes;
        int o = port * StateSize;
        if (port < 0 || o + StateSize > r.Length || good < 0 || good >= GoodsCount) return null;
        int market = r[o + 0x21];
        if (market >= Markets.Count) return null;
        int cat = CategoryOf(good);
        int idx = r[o + 0x0E + cat];
        int price = (idx + 50) * Markets[market].Pays[good] / 100;
        if (price <= 0 || gold * 100 < price) return null;
        int amount = Math.Min(1000, gold * 100 / price);
        r[o + 0x0E + cat] = (byte)(idx + Math.Min(100 - idx, rand(3) + 3));
        States = [.. Enumerable.Range(0, r.Length / StateSize).Select(k => ParseState(r, k * StateSize))];
        return (amount, price * amount / 100);
    }

    /// <summary>
    /// 달이 바뀔 때 항구 형편 백을 고친다(<c>0x0042B8A0</c>).
    /// 경제(+0)에 경제 투자(+2) / 300, 공업(+4)에 공업 투자(+6) / 300 을 더해 1000 에서 자르고 투자는 0 으로.
    /// 값 지수 열(+0x0E)은 저마다 1~5 씩 50 쪽으로 간다(50 을 넘으면 빼고 아니면 더한다).
    /// </summary>
    /// <param name="rand">0 이상 n 미만을 돌려주는 주사위(<c>0x0044E0D2</c>).</param>
    public void NewMonth(Func<int, int> rand)
    {
        var r = StateBytes;
        for (int o = 0; o + StateSize <= r.Length; o += StateSize)
        {
            for (int k = 0; k < 2; k++)
            {
                int at = o + k * 4;
                int v = Math.Min(EconomyCap, BitConverter.ToUInt16(r, at) + BitConverter.ToUInt16(r, at + 2) / InvestDivisor);
                r[at] = (byte)v; r[at + 1] = (byte)(v >> 8);
                r[at + 2] = 0; r[at + 3] = 0;
            }
            for (int i = 0; i < 10; i++)
            {
                int d = rand(5) + 1, v = r[o + 0x0E + i];
                r[o + 0x0E + i] = (byte)(v > 50 ? v - d : v + d);
            }
        }
        States = [.. Enumerable.Range(0, r.Length / StateSize).Select(k => ParseState(r, k * StateSize))];
    }

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
        r[o + 0x22], r[o + 0x23] == 0xFF ? -1 : r[o + 0x23], r[o + 0x1B]);

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

    /// <summary>
    /// 그 항구 교역소가 파는 것 — (교역품, 값). 특산품은 경제가 +0x1B x 10 이상이고 그 갈래 지수가 90 아래일 때만 맨 뒤에 붙는다
    /// (<c>0x00420460</c>: 지수가 높으면 「다 팔렸다」 쪽 말 4, 없거나 경제가 모자라면 말 3 이다).
    /// </summary>
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
        if (SpecialStatus(port) > 0)
            list.Add((s.Special, Price(s.SpecialBase, s.PriceIndex[CategoryOf(s.Special)], permit)));
        return list;
    }

    /// <summary>
    /// 그 항구 특산품 형편(<c>0x00420460</c>) — −1 없음(특산품이 없거나 경제 &lt; +0x1B x 10, 주인 말 3),
    /// 0 품절(그 갈래 지수 ≥ 90, 말 4), 1 판다(말 5).
    /// </summary>
    public int SpecialStatus(int port)
    {
        if (port < 0 || port >= States.Count) return -1;
        var s = States[port];
        if (s.Special < 0 || s.Special >= GoodsCount || s.SpecialNeed * 10 > s.Economy) return -1;
        return s.PriceIndex[CategoryOf(s.Special)] < SpecialIndexLimit ? 1 : 0;
    }

    /// <summary>
    /// 그 항구 교역소가 쳐 주는 값 — <c>0x004210E0</c>. 그 항구 특산품이면 바탕의 절반 셈(<c>/200</c>)이다.
    /// </summary>
    /// <summary>
    /// 그 교역품을 가장 비싸게 사 주는 항구(<c>0x00443680</c>) — 파는 값(<see cref="SellPrice"/>)이 가장 큰 항구. 없으면 -1.
    /// 원본은 항구 기록 +0x15 의 0x40 이 선 항구만 견준다.
    /// </summary>
    public int BestSellPort(int good, int ports, Func<int, bool>? counts = null)
    {
        int best = -1, price = 0;
        for (int p = 0; p < ports && p < States.Count; p++)
        {
            if (counts != null && !counts(p)) continue;
            int v = SellPrice(p, good);
            if (v > price) { price = v; best = p; }
        }
        return best;
    }

    public int SellPrice(int port, int good)
    {
        if (port < 0 || port >= States.Count || good < 0 || good >= GoodsCount) return 0;
        var s = States[port];
        int index = s.PriceIndex[CategoryOf(good)] + 50;
        if (good == s.Special) return s.SpecialBase * index / 200;
        return s.Zone < Markets.Count ? Markets[s.Zone].Pays[good] * index / 100 : 0;
    }

    /// <summary>보급 갈래 — 항구 「보급」의 넷(<c>MENU.DAT</c> 목록 21).</summary>
    public static readonly string[] SupplyNames = ["물", "식량", "자재", "탄약"];

    /// <summary>
    /// 보급 한 단(열 통) 값 — <c>0x00422E80</c>. 물은 공짜, 식량은 <c>(식품 지수 + 50) x 20 / 100</c>,
    /// 자재는 <c>(기타 지수 + 50) x 18 / 100</c>, 탄약은 <c>(식품 지수 + 50) x 120 / 100</c>
    /// (원본도 탄약에 식품 지수 바이트 <c>+0x10</c> 을 쓴다).
    /// </summary>
    public int SupplyPrice(int port, int kind)
    {
        if (port < 0 || port >= PortTable.Expected) return 0;
        // 100 넘는 작은 항구는 형편이 없어 고정 기록(0x00459D00, 값 지수 모두 50)을 쓴다(0x00435EB6).
        int food = (port < States.Count ? States[port].PriceIndex[2] : 50) + 50, other = (port < States.Count ? States[port].PriceIndex[9] : 50) + 50;
        return kind switch
        {
            1 => food * 20 / 100,
            2 => other * 18 / 100,
            3 => food * 120 / 100,
            _ => 0,
        };
    }

    private static int Price(int baseValue, int index, bool permit) =>
        baseValue * (index + 50) / 100 * (permit ? 10 : 12) / 10;
}
