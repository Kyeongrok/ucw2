namespace Uw2.Game.Engine;

/// <summary>
/// 시나리오가 들여다보는 게임 기억 — 가리키개 명령 <c>D0 · DC p t r f</c> 가 표 t 의 r 번 기록 f 바이트 자리를 가리키개 p 에 둔다
/// (<c>KOUKAI2.EXE 0x0043D510</c>). 변수 갈래 1 은 그 자리를 읽고 쓴다.
/// </summary>
/// <remarks>
/// 표 스물(번호 → 원본 자리, 기록 크기). 자리 셈은 원본 그대로 「바탕 + r x 크기 + f」 이고 기록 크기가 0 이면 r 을 안 쓴다.
/// <code>
///    0  0x0045E268  32      7  0x0046F8C8 …  9(r &lt; 10, 그 위는 0x0049F9D0)   14  0x0045FF88 128  시장(SDATA 0x605A)
///    1  0x0045A1E8  14      8  0x0045BE08 0x85  함대(SDATA 0x247B)          15  0x00460608   8
///    2  0x0049F5A8   0  돈(+0) · 주인공(+7)     9  0x004610B0  30  제 배 곁(보급 · 짐)   16  0x00460620   7
///    3  0x0045A240  50  사람(+0x2C 함대)        10  0x00461560  22  배 기록          17  0x004608E0  20
///    4  0x0045B9B0  22                         11  0x00461AF8  12               18  0x0049F5B8   0  (불러 온 기록)
///    5  0x0045BB68  19                         12  0x0045E5E0  22               19  0x0046EDE8   4
///    6  0x0045BDA8   0                         13  0x0045F110  37  항구 형편(SDATA 0x66DA)
/// </code>
/// 여기서는 표마다 바이트 배열 하나를 두고 아는 표(돈 · 주인공 · 사람 · 함대 · 제 배 · 시장 · 항구 형편)만 채운다. 나머지는 0 으로 읽힌다.
/// 낱말은 원본처럼 작은 끝(little endian)이다.
/// </remarks>
public sealed class ScenarioMemory
{
    public const int TableCount = 20, GoldTable = 2, PeopleTable = 3, FleetTable = 8, PlayerShipTable = 9,
                     MarketTable = 14, PortStateTable = 13;

    /// <summary>표 2 +7 — 주인공 사람 번호(<c>0x0049F5AF</c>). 원본은 이것 + 1 로 <c>SNRn</c> 을 고른다(<c>0x00415AD9</c>).</summary>
    public const int HeroField = 7;

    /// <summary>표마다 기록 크기(<c>0x0043D510</c> 의 곱).</summary>
    public static readonly int[] Stride = [32, 14, 0, 50, 22, 19, 0, 9, 0x85, 30, 22, 12, 22, 37, 128, 8, 7, 20, 0, 4];

    /// <summary>가리키개 — 표와 그 안의 바이트 자리.</summary>
    public readonly record struct Pointer(int Table, int Offset);

    private readonly byte[][] _tables = new byte[TableCount][];

    public ScenarioMemory()
    {
        for (int t = 0; t < TableCount; t++)
            _tables[t] = new byte[Math.Max(Stride[t], 1) * 256 + 256];
    }

    public static Pointer At(int table, int record, int field) =>
        new(table, (table < TableCount && Stride[table] > 0 ? record * Stride[table] : 0) + field);

    /// <summary>표 한 벌을 날바이트로 채운다(넘치면 자른다).</summary>
    public void Fill(int table, ReadOnlySpan<byte> raw)
    {
        var t = _tables[table];
        raw[..Math.Min(raw.Length, t.Length)].CopyTo(t);
    }

    public int Read(Pointer p, bool word)
    {
        if (!Ok(p, word)) return 0;
        var t = _tables[p.Table];
        return word ? t[p.Offset] | t[p.Offset + 1] << 8 : t[p.Offset];
    }

    public void Write(Pointer p, int v, bool word)
    {
        if (!Ok(p, word)) return;
        var t = _tables[p.Table];
        t[p.Offset] = (byte)v;
        if (word) t[p.Offset + 1] = (byte)(v >> 8);
    }

    public int ReadDword(Pointer p) => Read(p, true) | Read(p with { Offset = p.Offset + 2 }, true) << 16;

    public void WriteDword(Pointer p, int v)
    {
        Write(p, v & 0xFFFF, true);
        Write(p with { Offset = p.Offset + 2 }, (v >> 16) & 0xFFFF, true);
    }

    /// <summary>돈 — 표 2 의 맨 앞 dword(<c>0x0049F5A8</c>).</summary>
    public int Gold
    {
        get => ReadDword(new Pointer(GoldTable, 0));
        set => WriteDword(new Pointer(GoldTable, 0), value);
    }

    private bool Ok(Pointer p, bool word) =>
        p.Table is >= 0 and < TableCount && p.Offset >= 0 && p.Offset + (word ? 1 : 0) < _tables[p.Table].Length;
}
