namespace Uw2.Game.Engine;

/// <summary>
/// 시나리오가 들여다보는 게임 기억 — 가리키개 명령 <c>D0 · DC p t r f</c> 가 표 t 의 r 번 기록 f 바이트 자리를 가리키개 p 에 둔다
/// (<c>KOUKAI2.EXE 0x0043D510</c>). 변수 갈래 1 은 그 자리를 읽고 쓴다.
/// </summary>
/// <remarks>
/// 표 스물(번호 → 원본 자리, 기록 크기). 자리 셈은 원본 그대로 「바탕 + r x 크기 + f」 이고 기록 크기가 0 이면 r 을 안 쓴다.
/// <code>
///    0  0x0045E268  32  나라   7  0x0046F8C8 …  9(r &lt; 10, 그 위는 0x0049F9D0)   14  0x0045FF88 128  시장(SDATA 0x605A)
///    1  0x0045A1E8  14  주인공(+0x0D 작위)  8  0x0045BE08 0x85  함대(SDATA 0x247B)          15  0x00460608   8
///    2  0x0049F5A8   0  돈(+0) · 주인공(+7)     9  0x004610B0  30  제 배 곁(보급 · 짐)   16  0x00460620   7
///    3  0x0045A240  50  사람(+0x2C 함대)        10  0x00461560  22  배 기록          17  0x004608E0  20
///    4  0x0045B9B0  22                         11  0x00461AF8  12               18  0x0049F5B8   0  (불러 온 기록)
///    5  0x0045BB68  19                         12  0x0045E5E0  22               19  0x0046EDE8   4
///    6  0x0045BDA8   0  비율 · 동료(SDATA 0x48D9) 13  0x0045F110  37  항구 형편(SDATA 0x66DA)
/// </code>
/// 여기서는 표마다 바이트 배열 하나를 두고 아는 표(돈 · 주인공 · 사람 · 함대 · 제 배 · 시장 · 항구 형편)만 채운다. 나머지는 0 으로 읽힌다.
/// 낱말은 원본처럼 작은 끝(little endian)이다.
/// </remarks>
public sealed class ScenarioMemory
{
    public const int TableCount = 20, NationTable = 0, GoldTable = 2, MiscTable = 6, PortTable = 12, PeopleTable = 3, FleetTable = 8, PlayerShipTable = 9,
                     MarketTable = 14, PortStateTable = 13;

    /// <summary>표 2 +7 — 주인공 사람 번호(<c>0x0049F5AF</c>). 원본은 이것 + 1 로 <c>SNRn</c> 을 고른다(<c>0x00415AD9</c>).</summary>
    public const int HeroField = 7;

    /// <summary>표 1 — 주인공마다 14바이트(0x0045A1E8, 세이브에서 불러 온다 0x004035C6). +0x0D 가 작위(0 없음 · 1 기사 … 9 공작, 이름은 DATA1.LZW 16 · 17번).</summary>
    public const int HeroTable = 1, RankField = 0x0D;

    /// <summary>표마다 기록 크기(<c>0x0043D510</c> 의 곱).</summary>
    public static readonly int[] Stride = [32, 14, 0, 50, 22, 19, 0, 9, 0x85, 30, 22, 12, 22, 37, 128, 8, 7, 20, 0, 4];

    /// <summary>가리키개 — 표와 그 안의 바이트 자리.</summary>
    public readonly record struct Pointer(int Table, int Offset);

    /// <summary>
    /// 글 표 — 원본의 실제 글 자리(포인터가 가리키는 곳)를 대신한다. 표 19(<c>0x0046EDE8</c>)처럼 dword 포인터를 담는 표는
    /// <c>표 번호 &lt;&lt; 24 | 자리</c> 꼴로 이 표를 가리킨다(<see cref="Decode"/>).
    /// </summary>
    public const int StringTable = TableCount, Stored = TableCount + 1, GoodsNameTable = 19, NameSize = 16;

    private readonly byte[][] _tables = new byte[Stored][];

    public ScenarioMemory()
    {
        for (int t = 0; t < TableCount; t++)
            _tables[t] = new byte[Math.Max(Stride[t], 1) * 256 + 256];
        _tables[StringTable] = new byte[NameSize * 256];
    }

    /// <summary>
    /// 항구 표(표 12, 22바이트) — [x][y][이름 17(cp949)][나라]. 시나리오가 +4(이름) · +0x15(나라)를 짚는다.
    /// </summary>
    public void FillPorts(IEnumerable<(int X, int Y, string Name, byte Nation)> ports)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cp949 = System.Text.Encoding.GetEncoding(949);
        int r = 0;
        foreach (var (x, y, name, nation) in ports)
        {
            var rec = new byte[22];
            rec[0] = (byte)x; rec[1] = (byte)(x >> 8); rec[2] = (byte)y; rec[3] = (byte)(y >> 8);
            var bytes = cp949.GetBytes(name);
            Array.Copy(bytes, 0, rec, 4, Math.Min(16, bytes.Length));
            rec[0x15] = nation;
            for (int i = 0; i < 22; i++) Write(At(PortTable, r, i), rec[i], false);
            r++;
        }
    }

    /// <summary>dword 포인터 값 → 가리키개(<c>표 번호 &lt;&lt; 24 | 자리</c>).</summary>
    public static Pointer Decode(int v) => new((v >> 24) & 0xFF, v & 0xFFFFFF);

    /// <summary>
    /// 교역품 이름 — 표 19 는 이름 포인터 마흔여섯(4바이트씩)이다. 이름은 글 표에 열여섯 바이트씩 cp949 로 두고
    /// 표 19 에 그 자리를 가리키는 값을 쓴다. 시나리오는 <c>D0 → 넣기(받을 곳 3, 포인터 따라가기)</c>로 이름을 얻는다.
    /// </summary>
    public void FillGoodsNames(IReadOnlyList<string> names)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cp949 = System.Text.Encoding.GetEncoding(949);
        for (int i = 0; i < names.Count && i < 256; i++)
        {
            var bytes = cp949.GetBytes(names[i]);
            for (int k = 0; k < NameSize; k++)
                Write(new Pointer(StringTable, i * NameSize + k), k < bytes.Length && k < NameSize - 1 ? bytes[k] : 0, false);
            WriteDword(At(GoodsNameTable, i, 0), StringTable << 24 | i * NameSize);
        }
    }

    /// <summary>표 스물의 사본 — 세이브에 담는다.</summary>
    public byte[][] Export() => [.. _tables.Select(t => (byte[])t.Clone())];

    public void Import(byte[][] tables)
    {
        for (int t = 0; t < Stored && t < tables.Length; t++) Fill(t, tables[t]);
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
        p.Table is >= 0 and < Stored && p.Offset >= 0 && p.Offset + (word ? 1 : 0) < _tables[p.Table].Length;
}
