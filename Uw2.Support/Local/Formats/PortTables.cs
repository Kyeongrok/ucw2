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
