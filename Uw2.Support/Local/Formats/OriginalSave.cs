using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 원본 세이브 <c>KOUKAI2.DAT</c> — 머리 0x97(1 + 칸 이름 15 x 10) 뒤에 칸 열(0x837B 씩). 쓰는 손은 <c>0x004161A0</c>, 칸 몸통은
/// <c>0x00415360</c> 이 0x807B 바이트를 차례로 쓰고 그 뒤 0x300 바이트(<c>0x0046E498</c>)를 붙인다. 몸통은 <c>SDATA.DAT</c> 와 같은 자리를 쓴다(docs/23).
/// </summary>
/// <remarks>
/// <code>
///   칸 이름  「 2/Aug/1522」 + 0 · 머문 항구(0xFF 바다) · 0x0045A1E0 갈래
///   +0x05    해 − 1501      +0x06 달(0 부터)  +0x07 날(0 부터)  +0x08 시각(0 ~ 71)  +0x09 머문 항구
///   +0x37    주인공 시나리오 장 · +0x38 절 · +0x39 깃발 8 · +0x41 변수 0x80 · +0xC1 가리키개 0x100
///   +0x1C1   SNR0 장 · +0x1C2 절 · +0x1C3 깃발 8 · +0x1CB 변수 0x80 · +0x24B 가리키개 0x100
///   +0x241F  돈(dword) · 은행 낱말 x 100 + 바이트 · +7 주인공 사람 번호
///   +0x4FF8  도크 배 칸 9 x 30(배 칸 10 ~ 39, 0x0049F9D0)   +0x5106 칸 곁 30 x 40(0x004610B0)
/// </code>
/// </remarks>
public sealed class OriginalSave
{
    public const int HeadSize = 0x97, LabelSize = 15, SlotSize = 0x837B, BodySize = 0x807B, SlotCount = 10;

    public const int HeroChapter = 0x37, HeroSection = 0x38, HeroFlags = 0x39, HeroVars = 0x41, HeroPointers = 0xC1;
    public const int CommonChapter = 0x1C1, CommonSection = 0x1C2, CommonFlags = 0x1C3, CommonVars = 0x1CB, CommonPointers = 0x24B;
    public const int GoldOffset = 0x241F, DockSlotOffset = 0x4FF8, ShipSideOffset = 0x5106, ShipSideSize = 30;
    public const int PortStateOffset = 0x66DA, PortStateLength = 0xE74, MarketOffset = 0x605A, MarketLength = 0x680;
    public const int UsedShipOffset = 0x6042, UsedShipLength = 0x18, PortTableOffset = 0x754E, PortTableLength = 0xB2C;

    /// <summary>
    /// 몸통 덩이와 그 원본 자리 — 시나리오 기억 표(<c>ScenarioMemory</c> 번호)로 옮길 것. 가리키개를 표 자리로 바꿀 때도 쓴다.
    /// </summary>
    public static readonly (int Table, int Offset, int Length, int Address)[] Tables =
    [
        (0, StartData.NationOffset, 0xE0, 0x0045E268),
        (1, StartData.HeroOffset, 0x54, 0x0045A1E8),
        (2, GoldOffset, 8, 0x0049F5A8),
        (3, StartData.PersonOffset, 0x1770, 0x0045A240),
        (4, StartData.PatronOffset, 0x1B8, 0x0045B9B0),
        (5, StartData.BarmaidOffset, 0x23A, 0x0045BB68),
        (6, StartData.MiscOffset, 0x5D, 0x0045BDA8),
        (8, StartData.FleetOffset, 0x245E, 0x0045BE08),
        (9, ShipSideOffset, 0x4B0, 0x004610B0),
        (10, StartData.ShipRecordOffset, 0x596, 0x00461560),
        (11, StartData.ShipTypeOffset, 0x12C, 0x00461AF8),
        (12, PortTableOffset, PortTableLength, 0x0045E5E0),
        (13, PortStateOffset, PortStateLength, 0x0045F110),
        (14, MarketOffset, MarketLength, 0x0045FF88),
        (15, UsedShipOffset, UsedShipLength, 0x00460608),
        (16, StartData.DiscoveryOffset, 0x2BC, 0x00460620),
        (17, StartData.ItemOffset, 0x7D0, 0x004608E0),
    ];

    public sealed record Slot(int Number, string Label, byte[] Body, byte[] Log)
    {
        public int Year => Body[5];
        public int Month => Body[6];
        public int Day => Body[7];
        public int Time => Body[8];
        public int Port => Body[9] == 0xFF ? -1 : Body[9];
        public int HeroPerson => Body[GoldOffset + 7];
        public int Gold => BitConverter.ToInt32(Body, GoldOffset);
        public int Balance => BitConverter.ToUInt16(Body, GoldOffset + 4) * 100 + Body[GoldOffset + 6];

        public int Word(int at) => BitConverter.ToUInt16(Body, at);
        public int Short(int at) => BitConverter.ToInt16(Body, at);
    }

    public IReadOnlyList<Slot?> Slots { get; }

    private OriginalSave(IReadOnlyList<Slot?> slots) => Slots = slots;

    public static OriginalSave Parse(byte[] d)
    {
        if (d.Length < HeadSize + SlotSize)
            throw new InvalidDataException($"KOUKAI2.DAT 이 너무 짧습니다 ({d.Length})");
        var slots = new Slot?[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            int at = HeadSize + i * SlotSize;
            if (at + BodySize > d.Length) break;
            var label = Encoding.ASCII.GetString(d, 1 + i * LabelSize, 11).TrimEnd('\0').Trim();
            if (label.Length == 0) continue;
            byte[] log = at + SlotSize <= d.Length ? d.AsSpan(at + BodySize, SlotSize - BodySize).ToArray() : [];
            slots[i] = new Slot(i, label, d.AsSpan(at, BodySize).ToArray(), log);   // 몸통 뒤 0x300 은 항해일지(0x0046E498)
        }
        return new OriginalSave(slots);
    }

    public static OriginalSave Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>원본 가리키개(메모리 자리) → (표, 자리). 모르는 자리면 null.</summary>
    public static (int Table, int Offset)? Pointer(int address)
    {
        foreach (var (table, _, length, at) in Tables)
            if (address >= at && address < at + length) return (table, address - at);
        return null;
    }
}
