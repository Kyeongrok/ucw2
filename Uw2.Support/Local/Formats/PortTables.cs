namespace Uw2.Support.Local.Formats;

/// <summary>
/// 항구 지도. <c>PORTMAP.LZW</c> 는 청크 <b>101개, 하나가 9,216바이트</b>다 —
/// <c>96 x 96 = 9216</c> 이라 <b>칸마다 한 바이트</b>이고 그 값이 칩 번호다.
/// </summary>
/// <remarks>
/// 칩이 16 x 16 이니 항구 한 곳이 1536 x 1536 점이다. 어느 벌을 쓰는지는
/// <see cref="PortChipNumbers"/> 가 가른다.
/// </remarks>
public sealed class PortMap
{
    /// <summary>항구지도 한 변(칸).</summary>
    public const int Size = 96;

    /// <summary>항구 수.</summary>
    public const int Count = 101;

    private readonly byte[][] _maps;

    private PortMap(byte[][] maps) => _maps = maps;

    /// <summary>항구 하나의 칸 배열(96 x 96).</summary>
    public byte[] this[int port] => _maps[port];

    /// <summary>읽은 항구 수.</summary>
    public int Ports => _maps.Length;

    /// <summary>
    /// 걸어 다닐 수 있는 칩. <b>바닥·길과 같은 색으로 그려진 칩</b>을 고른다.
    /// </summary>
    /// <remarks>
    /// 게임 표를 아직 못 찾아 그림에서 뽑는다. 항구 한 곳에 칩이 이백 가지 넘게 쓰이는데,
    /// 길은 곧은 토막 하나가 아니라 <b>모퉁이·가장자리까지 여러 벌</b>이라 개수로만 고르면
    /// 네거리에서 걸음이 막힌다. 그래서 이렇게 한다.
    /// <list type="number">
    ///   <item>가장 흔한 칩을 물로 본다.</item>
    ///   <item>물을 뺀 가장 흔한 둘을 <b>바닥</b>과 <b>길</b>로 본다.</item>
    ///   <item>그 둘이 쓰는 색인을 모아, <b>그 색으로만 거의 다 그려진 칩</b>을 밟을 수 있다고 본다.</item>
    /// </list>
    /// 건물·나무·물가는 딴 색이 섞여 있어 걸러진다.
    /// </remarks>
    public HashSet<byte> WalkableChips(int port, ChipSheet sheet)
    {
        var cells = _maps[port];
        var count = new int[256];
        foreach (byte c in cells) count[c]++;

        int water = Biggest(count, -1);
        int ground = Biggest(count, water);
        int road = Biggest(count, water, ground);

        // 바닥과 길이 쓰는 색인을 모은다.
        var ink = new bool[16];
        foreach (int t in (int[])[ground, road])
            if (t >= 0 && t < sheet.Count)
                for (int y = 0; y < ChipSheet.Size; y++)
                    for (int x = 0; x < ChipSheet.Size; x++)
                        ink[sheet[t, x, y] & 0xF] = true;

        var walk = new HashSet<byte>();
        for (int t = 0; t < sheet.Count && t < 256; t++)
        {
            if (t == water) continue;
            int same = 0;
            for (int y = 0; y < ChipSheet.Size; y++)
                for (int x = 0; x < ChipSheet.Size; x++)
                    if (ink[sheet[t, x, y] & 0xF]) same++;
            if (same >= ChipSheet.Size * ChipSheet.Size * WalkPurity) walk.Add((byte)t);
        }
        return walk;
    }

    /// <summary>칩이 이만큼 바닥·길 색으로만 그려져 있어야 밟을 수 있다고 본다.</summary>
    public const double WalkPurity = 0.92;

    private static int Biggest(int[] count, params int[] skip)
    {
        int best = -1;
        for (int i = 0; i < count.Length; i++)
        {
            if (Array.IndexOf(skip, i) >= 0) continue;
            if (best < 0 || count[i] > count[best]) best = i;
        }
        return best;
    }

    /// <summary>그 항구의 물 칩. 가장 흔한 것이 물이다.</summary>
    public byte WaterChip(int port)
    {
        var count = new int[256];
        foreach (byte c in _maps[port]) count[c]++;
        int water = 0;
        for (int i = 1; i < 256; i++) if (count[i] > count[water]) water = i;
        return (byte)water;
    }

    /// <summary>구워 둔 칸 배열들에서 바로 만든다.</summary>
    public static PortMap FromCells(byte[][] maps) => new(maps);

    /// <summary>파일에서 읽는다.</summary>
    public static PortMap Load(string path)
    {
        var chunks = LsArchive.ReadAll(path);
        foreach (var c in chunks)
            if (c.Length != Size * Size)
                throw new InvalidDataException($"항구지도가 {Size * Size}바이트가 아닙니다 ({c.Length})");
        return new PortMap(chunks);
    }
}

/// <summary>
/// 항구 칩 벌. <c>PORTCHIP.LZW</c> 는 청크 열넷 — <b>30,720바이트짜리 일곱 개</b>(각 240장)와
/// 4바이트짜리 일곱 개다. 4바이트짜리는 안 눌린 채로 들어 있다(딸린 값으로 보인다).
/// </summary>
public sealed class PortChipSets
{
    /// <summary>칩 벌 수. <see cref="PortChipNumbers"/> 의 값 범위(0~6)와 맞는다.</summary>
    public const int SetCount = 7;

    /// <summary>한 벌에 든 칩 수.</summary>
    public const int ChipsPerSet = 240;

    private readonly ChipSheet[] _sets;
    private readonly byte[][] _extras;

    private PortChipSets(ChipSheet[] sets, byte[][] extras) { _sets = sets; _extras = extras; }

    /// <summary>벌 하나.</summary>
    public ChipSheet this[int set] => _sets[set];

    /// <summary>벌 전부.</summary>
    public ChipSheet[] All => _sets;

    /// <summary>벌마다 딸린 4바이트. 아직 뜻을 모른다.</summary>
    public byte[] ExtraOf(int set) => _extras[set];

    /// <summary>파일에서 읽는다.</summary>
    public static PortChipSets Load(string path)
    {
        var chunks = LsArchive.ReadAll(path);
        var sets = new List<ChipSheet>();
        var extras = new List<byte[]>();
        foreach (var c in chunks)
        {
            if (c.Length >= ChipSheet.BytesPerChip) sets.Add(ChipSheet.Open(c));
            else extras.Add(c);
        }
        return new PortChipSets([.. sets], [.. extras]);
    }
}

/// <summary>
/// <c>CHIP_NO.DAT</c> — 100바이트, 값이 <b>0~6</b>. 색인이 항구 번호, 값이 그 항구가 쓰는
/// <see cref="PortChipSets">칩 벌</see>이다.
/// </summary>
/// <remarks>
/// <b>이 표를 처음에 세계지도 지형표로 잘못 봤다.</b> 값이 0~6 일곱 가지인 것이 3편의 칸당
/// 눈금표와 닮아 그랬는데, <c>PORTCHIP</c> 이 일곱 벌이고 <c>PORTMAP</c> 이 101장인 것을 보고
/// 바로잡았다. 번호가 지역순이라 <b>벌이 문화권을 가른다</b>.
/// <code>
///   0–26 섞임 0·2 · 27–41 → 1 · 42–71 → 3 · 72–80 → 2
///   81–93 섞임 3·6 · 94–97 → 4 · 98–99 → 5
/// </code>
/// 지도는 101장인데 표는 100칸이라 한 장이 남는다.
/// </remarks>
public sealed class PortChipNumbers
{
    /// <summary>표의 칸 수.</summary>
    public const int Count = 100;

    private readonly byte[] _sets;

    private PortChipNumbers(byte[] sets) => _sets = sets;

    /// <summary>그 항구가 쓰는 칩 벌(0~6).</summary>
    public int this[int port] => port >= 0 && port < _sets.Length ? _sets[port] : 0;

    /// <summary>날바이트에서 바로 만든다.</summary>
    public static PortChipNumbers FromBytes(byte[] sets) => new(sets);

    /// <summary>파일에서 읽는다.</summary>
    public static PortChipNumbers Load(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length != Count)
            throw new InvalidDataException($"CHIP_NO.DAT 이 {Count}바이트가 아닙니다 ({d.Length})");
        return new PortChipNumbers(d);
    }
}
