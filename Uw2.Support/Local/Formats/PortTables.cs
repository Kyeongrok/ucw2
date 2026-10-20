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

    /// <summary>벌마다 딸린 4바이트 — 걸음 경계(<see cref="WalkBounds"/>).</summary>
    public byte[] ExtraOf(int set) => _extras[set];

    /// <summary>벌마다 걸음 경계. 게임이 항구에 들 때 <c>0x0046EDDC</c> 에 넉 바이트를 옮겨 둔다(<c>0x004360C8</c>).</summary>
    public WalkBounds[] Bounds => [.. _extras.Select(WalkBounds.FromBytes)];

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

/// <summary>
/// 항구 칩 벌 하나의 걸음 경계 — <c>PORTCHIP.LZW</c> 의 4바이트 청크 <c>[0, a, b, c]</c>.
/// </summary>
/// <remarks>
/// 걸음 판정 <c>KOUKAI2.EXE 0x00432EB0</c> · <c>0x00433580</c> 이 이렇게 쓴다. 사람 그림은 2 x 2 칸이고
/// <b>밑줄 두 칸(왼발 · 오른발)</b>만 본다.
/// <code>
///   왼발 칩  &lt; b                          이어야 하고
///   오른발 칩 &lt; a  또는  b &lt;= 칩 &lt; c      이어야 걷는다
/// </code>
/// 곧 칩 번호가 쓰임새대로 늘어서 있다 — <c>0..a-1</c> 은 어디든 밟는 바닥, <c>a..b-1</c> 은 왼발만,
/// <c>b..c-1</c> 은 오른발만(오른쪽 · 왼쪽이 벽인 가장자리 칩), <c>c..</c> 는 건물 · 물 · 나무라 못 밟는다.
/// </remarks>
public readonly record struct WalkBounds(byte A, byte B, byte C)
{
    public static WalkBounds FromBytes(byte[] raw) =>
        raw.Length >= 4 ? new WalkBounds(raw[1], raw[2], raw[3]) : new WalkBounds(0, 0, 0);

    /// <summary>두 발이 밟은 칩으로 걸을 수 있는지.</summary>
    public bool CanStand(byte left, byte right) =>
        left < B && (right < A || (right >= B && right < C));
}

/// <summary>
/// 항구마다 건물 문 자리 — <c>ZA_DAT.DAT</c>, 2,424바이트 = 항구 101곳 x 문 12개 x (x, y).
/// </summary>
/// <remarks>
/// 항구에 들 때 <c>KOUKAI2.EXE 0x00436500</c> 이 <c>항구 번호 x 24</c> 자리에서 24바이트를 <c>0x00457450</c> 으로 읽는다
/// (번호가 100 이상이면 100번 줄). 걸을 때마다 <c>0x00432BF0</c> 이 사람 그림 왼위 칸(화면 자리 + 굴림)을
/// 열두 문과 견주어 같은 문의 차례를 건물 번호로 돌려준다. <c>(255, 255)</c> 는 그 건물이 없는 것이다.
///
/// 건물 번호의 이름은 <c>MENU.DAT</c> 의 열두 줄 목록(<c>0x9C7</c> 언저리) 그대로다.
/// </remarks>
public sealed class PortDoors
{
    public const int Ports = 101, PerPort = 12, Bytes = Ports * PerPort * 2;

    /// <summary>건물 번호 0~11 의 이름 — <c>MENU.DAT</c> 원문 그대로(「항」 · 「ITEM」 · 「NPC」 도 그대로 둔다).</summary>
    public static readonly string[] Names =
        ["교역소", "주점", "조선소", "항", "여관", "왕궁", "조합", "NPC", "은행", "ITEM", "교회", "점술가"];

    /// <summary>
    /// 그 시각(하루 72틱, 한 시간 3틱)에 건물이 열려 있는지 — <c>0x00431EFD</c> 의 갈래표 <c>0x0043212C</c>.
    /// 닫혀 있으면 「문이 잠겨 있습니다.」(말 <c>0x2FD</c>, <c>0x00432160</c>)다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   교역소 · 조선소 · 왕궁 · 조합 · 은행   12~59     (4시~20시)
    ///   주점                              12~23 만 닫음 (4시~8시)
    ///   ITEM                              6~8 · 24~59 (2시~3시 · 8시~20시)
    ///   교회                              12~71     (4시~24시)
    ///   점술가                            48~71     (16시~24시)
    ///   항 · 여관 · NPC                    늘
    /// </code>
    /// </remarks>
    public static bool IsOpen(int building, int tick) => building switch
    {
        0 or 2 or 5 or 6 or 8 => tick is >= 12 and < 60,
        1 => tick is < 12 or >= 24,
        9 => tick is >= 24 and < 60 || tick is >= 6 and < 9,
        10 => tick >= 12,
        11 => tick >= 48,
        _ => true,
    };

    /// <summary>잠긴 문 앞에서 하는 말(<c>MESSAGE.DAT 0x2FD</c>).</summary>
    public const string LockedText = "문이 잠겨 있습니다.";

    private readonly byte[] _raw;

    private PortDoors(byte[] raw) => _raw = raw;

    public static PortDoors FromBytes(byte[] raw)
    {
        if (raw.Length < Bytes) throw new InvalidDataException($"ZA_DAT.DAT 이 {Bytes}바이트가 아닙니다 ({raw.Length})");
        return new PortDoors(raw);
    }

    public static PortDoors Load(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>파일 원본.</summary>
    public byte[] Raw => _raw;

    /// <summary>그 항구의 문 열둘. 없는 건물은 null.</summary>
    public (int X, int Y)?[] Of(int port)
    {
        int row = Math.Clamp(port, 0, Ports - 1) * PerPort * 2;
        var doors = new (int X, int Y)?[PerPort];
        for (int i = 0; i < PerPort; i++)
        {
            byte x = _raw[row + i * 2], y = _raw[row + i * 2 + 1];
            doors[i] = x == 0xFF && y == 0xFF ? null : (x, y);
        }
        return doors;
    }
}
