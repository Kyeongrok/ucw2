using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 새 게임 자료 <c>SDATA.DAT</c>(33,922바이트) — 게임이 새로 시작할 때 이 파일을 통째로 읽어 표마다 옮겨 담는다
/// (<c>KOUKAI2.EXE 0x00403740</c> 언저리). 세이브 <c>KOUKAI2.DAT</c> 는 같은 짜임이 <c>0x837B</c> 바이트씩 열 벌이다.
/// </summary>
/// <remarks>
/// 지금은 항해 셈에 드는 두 표만 읽는다.
/// <code>
///   0x0CAF   사람   50바이트 x 120   메모리 0x0045A240 (0x0043ADC0 이 짚는다)
///   0x4936   선종   12바이트 x 25    메모리 0x00461AF8 (0x0043AEB0 이 짚는다)
/// </code>
/// 선종 표는 EXE 안에도 같은 값이 박혀 있다(파일 자리 <c>0x5FCF8</c>).
/// </remarks>
public sealed class StartData
{
    public const int PersonOffset = 0x0CAF, PersonSize = 50, PersonCount = 120;

    /// <summary>나라 표 — <c>0x7DD</c>, 32바이트 x 7(메모리 0x0045E268, 읽는 손 0x00403D20 의 마흔일곱째 fread). +0x0A 가 서울 항구.</summary>
    public const int NationOffset = 0x07DD, NationSize = 32, NationCount = 7, CapitalField = 0x0A;

    /// <summary>주인공 표 — <c>0x2427</c>, 14바이트 x 6(메모리 0x0045A1E8). +6 ~ +0x0C 일곱이 새 게임 100, +0x0D 작위.</summary>
    public const int HeroOffset = 0x2427, HeroSize = 14, HeroCount = 6;

    /// <summary>
    /// 함대 표 뒤 덩이 — <c>0x48D9</c>, 0x5D 바이트(메모리 0x0045BDA8). +0 바다에 나온 날 수 · +1 물 비율 · +2 식량 비율 ·
    /// +3 ~ +0x20 동료 서른 · +0x21 ~ +0x3E 그 급여 / 10.
    /// </summary>
    public const int MiscOffset = 0x48D9, MiscSize = 0x5D;

    /// <summary>아이템 표 — <c>0x55B6</c>, 20바이트 x 100(메모리 0x004608E0).</summary>
    public const int ItemOffset = 0x55B6, ItemSize = 20, ItemCount = 100;

    /// <summary>발견물 표 — <c>0x5D86</c>, 7바이트 x 100(메모리 0x00460620). 98 개를 쓴다.</summary>
    public const int DiscoveryOffset = 0x5D86, DiscoverySize = 7, DiscoveryCount = 100;

    /// <summary>「NPC」 건물 임자 표 — <c>0xAF7</c>, 22바이트 x 20(메모리 0x0045B9B0, 사람 표 바로 앞).</summary>
    public const int PatronOffset = 0xAF7, PatronSize = 22, PatronCount = 20;

    /// <summary>주점 여급 표 — <c>0x8BD</c>, 19바이트 x 30(메모리 0x0045BB68, 임자 표 바로 앞).</summary>
    public const int BarmaidOffset = 0x8BD, BarmaidSize = 19, BarmaidCount = 30;
    public const int ShipTypeOffset = 0x4936, ShipTypeSize = 12, ShipTypeCount = 25;
    public const int FleetOffset = 0x247B, FleetSize = 0x85, FleetCount = 70, SlotCount = 10, SlotSize = 9;

    /// <summary>
    /// 배 기록 — <c>0x4A62</c>, 22바이트 x 65 (메모리 0x00461560). 0~39 가 제 함대 몫(새 게임은 「０」「１」… 빈 것),
    /// 40~64 가 선종마다 본보기(이름이 곧 선종 이름)다. 배를 들이는 손 <c>0x0043DEA0(선종)</c> 이 본보기 <c>40 + 선종</c> 에서
    /// 이름 · 깃발을 베낀다.
    /// </summary>
    public const int ShipRecordOffset = 0x4A62, ShipRecordSize = 22, ShipTemplateFirst = 40;

    /// <summary>
    /// 선종 본보기 한 줄.
    /// </summary>
    /// <param name="Flags">+0x10 — 비트 0x10 이 「돛만 쓰는 배」. 갤리(경갤리 · 플랜더스 갤리 · 베네치안 갤리 · 관선)는 이 비트가 없어 노를 젓는다.</param>
    /// <param name="Cannons">+0x11 대포.</param>
    /// <param name="CrewRoom">+0x12 선원 자리(낱말).</param>
    /// <param name="CargoRoom">+0x14 짐 자리(낱말) = 짐칸 - 대포 - 선원 자리.</param>
    public readonly record struct ShipTemplate(int Type, string Name, int Flags, int Cannons, int CrewRoom, int CargoRoom)
    {
        public bool Oars => (Flags & 0x10) == 0;
    }

    /// <summary>함대 칸 하나(9바이트, 함대 +0x2B). <see cref="Present"/> 가 거짓이면 빈 칸이다.</summary>
    /// <param name="Crew">+0 선원(낱말).</param>
    /// <param name="Durability">+2 · +3 내구 지금 / 최대.</param>
    /// <param name="Upwind">+4 역풍 성능.</param>
    /// <param name="Sail">+5 돛 성능.</param>
    /// <param name="Ship">+7 배 번호(배 기록 0x00461560).</param>
    /// <param name="Flags">+8 — <c>&amp; 0x30 == 0x10</c> 이면 배가 있다.</param>
    public readonly record struct Slot(int Crew, int Durability, int MaxDurability, int Upwind, int Sail,
                                       int Field6, int Ship, int Flags)
    {
        public bool Present => (Flags & 0x30) == 0x10;
    }

    /// <summary>
    /// 함대 하나(0x85바이트, 메모리 <c>0x0045BE08</c> · <c>0x0043AE20</c> 이 짚는다). 새 게임의 일흔 함대다 —
    /// 나라(고향 항구)마다 열(주인공 하나 + 남 아홉), 60~69 는 해적이다.
    /// </summary>
    /// <param name="X">+0 · +2 칩 눈금 자리.</param>
    /// <param name="Admiral">+0x2A 제독(사람 번호).</param>
    /// <param name="Port">+0x26 머문 · 고향 항구.</param>
    /// <param name="Flags">+0x29 — 비트 0 이 「바다에 나와 있다」. 주인공 함대는 0 으로 시작한다.</param>
    /// <param name="TargetX">+0x04 · +0x06 목표 자리 — 새 게임에는 갈 항구 자리가 들어 있다.</param>
    /// <param name="Home">+0x1C — 움직임 갈래 0 에서는 닿은 뒤 돌아갈 항구 번호(고향)다.</param>
    public readonly record struct Fleet(int Index, int X, int Y, int Heading, int Admiral, int Port, int Flags,
                                        int TargetX, int TargetY, int Home, IReadOnlyList<Slot> Slots)
    {
        public bool Active => (Flags & 1) != 0;
        public int ShipCount => Slots.Count(s => s.Present);
    }

    /// <summary>
    /// 선종 하나. 자리 이름은 셈에서 쓰는 꼴로 붙였다 — <b>뜻을 못 굳힌 것은 이름 끝에 그렇다고 적었다.</b>
    /// </summary>
    /// <param name="Durability">+1 내구도. 배를 들일 때 칸의 지금·최대 내구로 옮긴다(<c>0x0042AC48</c>).</param>
    /// <param name="Upwind">+2 역풍 성능(백분). 칸 +4 로 옮겨 맞바람일 때 빠르기에 곱한다.</param>
    /// <param name="Sail">+3 돛 성능. 칸 +5 로 옮겨 늘 빠르기에 곱한다.</param>
    /// <param name="RequiredCrew">+5 항해에 드는 선원 수. 이만큼 돛에 붙어야 제 빠르기가 난다.</param>
    /// <param name="Capacity">+6 짐칸(낱말). 짐이 찰수록 느려진다.</param>
    /// <param name="MaxCrew">+8 실을 수 있는 대포의 위끝 — 「적재비율변경」 이 0 ~ 이것으로 묻는다(<c>0x004294DB</c>). 이름은 예전 짐작 그대로다.</param>
    /// <param name="SailKind">+9 돛 갈래 0~7. 돛 효율 표의 칸을 고른다.</param>
    /// <param name="Price">+10 값(낱말).</param>
    /// <param name="Field0">+0 드는 공업 / 10 — 조선소가 짓는 데 항구 공업이 이것 x 10 이상이어야 한다(0x00428958).</param>
    /// <param name="Field4">+4 선원 자리 위끝 / 10 — 「적재비율변경」 이 +5 ~ 이것 x 10 으로 묻는다(<c>0x0042949F</c>). 새 배 본보기는 넘기도 한다.</param>
    public readonly record struct ShipType(int Index, int Field0, int Durability, int Upwind, int Sail, int Field4,
                                           int RequiredCrew, int Capacity, int MaxCrew, int SailKind, int Price);

    /// <summary>
    /// 사람 하나. 이름 · 성 열세 바이트씩 뒤에 능력치 일곱이 온다.
    /// </summary>
    /// <param name="Seamanship">+0x1D 항해술. 빠르기에 <c>/75</c> 로 곱한다.</param>
    /// <param name="NavLevel">+0x24 항해 레벨. 빠르기에 <c>(10 + 레벨)/10</c> 로 곱한다.</param>
    /// <param name="BattleLevel">+0x25 전투 레벨.</param>
    public readonly record struct Person(int Index, string Name, string Family, int[] Stats,
                                         int Seamanship, int NavLevel, int BattleLevel);

    public IReadOnlyList<ShipType> ShipTypes { get; }
    public IReadOnlyList<Person> People { get; }
    public IReadOnlyList<Fleet> Fleets { get; }
    public IReadOnlyList<ShipTemplate> Templates { get; private init; } = [];

    /// <summary>
    /// 주인공(사람 번호 0~5)이 처음 받는 선종 — <c>SNRn.DAT</c> 첫 이벤트의 배 주기 명령 <c>F9 선종 _</c>(<see cref="Scenario"/>).
    /// 못 읽었으면 빈 것.
    /// </summary>
    public IReadOnlyList<int> HeroShips { get; init; } = [];

    /// <summary>주인공 첫 돈 — <see cref="Scenario.FirstGold"/>(어림).</summary>
    public IReadOnlyList<int> HeroGold { get; init; } = [];

    /// <summary>함대 표 날바이트(70 x 0x85) — 구워 둘 때 쓴다.</summary>
    public byte[] FleetBytes { get; }

    /// <summary>사람 표 날바이트(120 x 50, 메모리 <c>0x0045A240</c>) — 시나리오 기억 표 3 이 쓴다. +0x2C 가 탄 함대.</summary>
    public byte[] PeopleBytes { get; init; } = [];

    /// <summary>나라 표 날바이트(7 x 32) — 시나리오 기억 표 0.</summary>
    public byte[] NationBytes { get; init; } = [];

    /// <summary>주인공 표 날바이트(6 x 14) — 시나리오 기억 표 1.</summary>
    public byte[] HeroBytes { get; init; } = [];

    /// <summary>함대 뒤 덩이 날바이트(0x5D) — 시나리오 기억 표 6.</summary>
    public byte[] MiscBytes { get; init; } = [];

    /// <summary>아이템 표 날바이트(100 x 20) — 시나리오 기억 표 17.</summary>
    public byte[] ItemBytes { get; init; } = [];

    /// <summary>발견물 표 날바이트(100 x 7) — 시나리오 기억 표 16.</summary>
    public byte[] DiscoveryBytes { get; init; } = [];

    /// <summary>임자 표 날바이트(20 x 22) — 시나리오 기억 표 4.</summary>
    public byte[] PatronBytes { get; init; } = [];

    /// <summary>여급 표 날바이트(30 x 19) — 시나리오 기억 표 5.</summary>
    public byte[] BarmaidBytes { get; init; } = [];

    /// <summary>나라마다 서울 항구 — 원본은 여기서 투자를 받지 않는다(<c>0x00431A70</c>).</summary>
    public IEnumerable<int> Capitals =>
        Enumerable.Range(0, NationBytes.Length / NationSize).Select(n => (int)NationBytes[n * NationSize + CapitalField]);

    private StartData(IReadOnlyList<ShipType> types, IReadOnlyList<Person> people, byte[] fleets)
    {
        ShipTypes = types; People = people; FleetBytes = fleets; Fleets = ParseFleets(fleets);
    }

    /// <summary>구워 둔 표로 만든다(<see cref="AssetPack"/>).</summary>
    public static StartData FromTables(IReadOnlyList<ShipType> types, IReadOnlyList<Person> people, byte[]? fleets = null,
                                       IReadOnlyList<ShipTemplate>? templates = null, IReadOnlyList<int>? heroShips = null,
                                       IReadOnlyList<int>? heroGold = null, byte[]? peopleBytes = null,
                                       byte[]? nationBytes = null, byte[]? heroBytes = null, byte[]? miscBytes = null,
                                       byte[]? itemBytes = null, byte[]? discoveryBytes = null,
                                       byte[]? patronBytes = null, byte[]? barmaidBytes = null) =>
        new(types, people, fleets ?? [])
        {
            Templates = templates ?? [], HeroShips = heroShips ?? [], HeroGold = heroGold ?? [], PeopleBytes = peopleBytes ?? [],
            NationBytes = nationBytes ?? [], HeroBytes = heroBytes ?? [], MiscBytes = miscBytes ?? [],
            ItemBytes = itemBytes ?? [], DiscoveryBytes = discoveryBytes ?? [], PatronBytes = patronBytes ?? [], BarmaidBytes = barmaidBytes ?? [],
        };

    /// <summary>함대 표 날바이트(0x85 x 70)를 읽는다 — 세이브 칸 것도 같은 꼴이다.</summary>
    public static Fleet[] ParseFleets(byte[] raw)
    {
        int n = Math.Min(FleetCount, raw.Length / FleetSize);
        var fleets = new Fleet[n];
        for (int k = 0; k < n; k++)
        {
            int o = k * FleetSize;
            var slots = new Slot[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                int s = o + 0x2B + i * SlotSize;
                slots[i] = new Slot(BitConverter.ToUInt16(raw, s), raw[s + 2], raw[s + 3], raw[s + 4], raw[s + 5],
                                    raw[s + 6], raw[s + 7], raw[s + 8]);
            }
            fleets[k] = new Fleet(k, BitConverter.ToInt16(raw, o), BitConverter.ToInt16(raw, o + 2), raw[o + 0x1A],
                                  raw[o + 0x2A], raw[o + 0x26], raw[o + 0x29],
                                  BitConverter.ToInt16(raw, o + 4), BitConverter.ToInt16(raw, o + 6), raw[o + 0x1C],
                                  slots);
        }
        return fleets;
    }

    /// <summary>
    /// 칸의 선종 — 배 번호(+7)가 본보기(40 ~ 64)면 그 배 기록의 +0x0F 를 쓴다. 남 함대는 다 본보기를 짚는다
    /// (카라벨 라티나와 다우는 내구 · 역풍 · 돛이 같아 값으로는 못 가른다).
    /// 그 밖(제 함대 몫 0 ~ 39)은 내구 최대 · 역풍 · 돛이 다 맞는 첫 선종으로 어림한다.
    /// </summary>
    public int TypeOf(Slot slot)
    {
        int t0 = slot.Ship - ShipTemplateFirst;
        if (t0 >= 0 && t0 < Templates.Count && Templates[t0].Type < ShipTypeCount) return Templates[t0].Type;
        foreach (var t in ShipTypes)
            if (t.Durability == slot.MaxDurability && t.Upwind == slot.Upwind && t.Sail == slot.Sail) return t.Index;
        return 0;
    }

    public static StartData Load(string path)
    {
        var s = FromBytes(File.ReadAllBytes(path));
        string? game = Path.GetDirectoryName(path);
        return game == null ? s : new StartData(s.ShipTypes, s.People, s.FleetBytes)
        {
            Templates = s.Templates,
            PeopleBytes = s.PeopleBytes,
            NationBytes = s.NationBytes,
            HeroBytes = s.HeroBytes,
            MiscBytes = s.MiscBytes,
            ItemBytes = s.ItemBytes,
            DiscoveryBytes = s.DiscoveryBytes,
            PatronBytes = s.PatronBytes,
            BarmaidBytes = s.BarmaidBytes,
            HeroShips = Scenario.HeroShips(game),
            HeroGold = Scenario.HeroGold(game),
        };
    }

    public static StartData FromBytes(byte[] d)
    {
        if (d.Length < ShipTypeOffset + ShipTypeSize * ShipTypeCount)
            throw new InvalidDataException($"SDATA.DAT 이 너무 짧습니다 ({d.Length})");

        var types = new ShipType[ShipTypeCount];
        for (int i = 0; i < ShipTypeCount; i++)
        {
            int o = ShipTypeOffset + i * ShipTypeSize;
            types[i] = new ShipType(i, d[o], d[o + 1], d[o + 2], d[o + 3], d[o + 4], d[o + 5],
                                    BitConverter.ToUInt16(d, o + 6), d[o + 8], d[o + 9],
                                    BitConverter.ToUInt16(d, o + 10));
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        string Text(int at) => cp949.GetString(d, at, 13).Split('\0')[0];
        var people = new Person[PersonCount];
        for (int i = 0; i < PersonCount; i++)
        {
            int o = PersonOffset + i * PersonSize;
            var stats = d.AsSpan(o + 0x1C, 7).ToArray().Select(b => (int)b).ToArray();
            people[i] = new Person(i, Text(o), Text(o + 13), stats, d[o + 0x1D], d[o + 0x24], d[o + 0x25]);
        }
        var fleets = d.AsSpan(FleetOffset, FleetSize * FleetCount).ToArray();
        var templates = new ShipTemplate[ShipTypeCount];
        for (int t = 0; t < ShipTypeCount; t++)
        {
            int o = ShipRecordOffset + (ShipTemplateFirst + t) * ShipRecordSize;
            templates[t] = new ShipTemplate(d[o + 15], cp949.GetString(d, o, 15).Split('\0')[0],
                                            d[o + 0x10], d[o + 0x11], BitConverter.ToUInt16(d, o + 0x12),
                                            BitConverter.ToUInt16(d, o + 0x14));
        }
        return new StartData(types, people, fleets)
        {
            Templates = templates,
            PeopleBytes = d.AsSpan(PersonOffset, PersonSize * PersonCount).ToArray(),
            NationBytes = d.AsSpan(NationOffset, NationSize * NationCount).ToArray(),
            HeroBytes = d.AsSpan(HeroOffset, HeroSize * HeroCount).ToArray(),
            MiscBytes = d.AsSpan(MiscOffset, MiscSize).ToArray(),
            ItemBytes = d.AsSpan(ItemOffset, ItemSize * ItemCount).ToArray(),
            DiscoveryBytes = d.AsSpan(DiscoveryOffset, DiscoverySize * DiscoveryCount).ToArray(),
            PatronBytes = d.AsSpan(PatronOffset, PatronSize * PatronCount).ToArray(),
            BarmaidBytes = d.AsSpan(BarmaidOffset, BarmaidSize * BarmaidCount).ToArray(),
        };
    }
}
