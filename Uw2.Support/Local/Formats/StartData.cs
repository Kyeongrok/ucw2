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
    public const int ShipTypeOffset = 0x4936, ShipTypeSize = 12, ShipTypeCount = 25;

    /// <summary>
    /// 선종 하나. 자리 이름은 셈에서 쓰는 꼴로 붙였다 — <b>뜻을 못 굳힌 것은 이름 끝에 그렇다고 적었다.</b>
    /// </summary>
    /// <param name="Durability">+1 내구도. 배를 들일 때 칸의 지금·최대 내구로 옮긴다(<c>0x0042AC48</c>).</param>
    /// <param name="Upwind">+2 역풍 성능(백분). 칸 +4 로 옮겨 맞바람일 때 빠르기에 곱한다.</param>
    /// <param name="Sail">+3 돛 성능. 칸 +5 로 옮겨 늘 빠르기에 곱한다.</param>
    /// <param name="RequiredCrew">+5 항해에 드는 선원 수. 이만큼 돛에 붙어야 제 빠르기가 난다.</param>
    /// <param name="Capacity">+6 짐칸(낱말). 짐이 찰수록 느려진다.</param>
    /// <param name="MaxCrew">+8 태울 수 있는 선원 수(로 보인다).</param>
    /// <param name="SailKind">+9 돛 갈래 0~7. 돛 효율 표의 칸을 고른다.</param>
    /// <param name="Price">+10 값(낱말).</param>
    /// <param name="Field0">+0 뜻 미확정.</param>
    /// <param name="Field4">+4 뜻 미확정(대포 수로 보인다).</param>
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

    private StartData(IReadOnlyList<ShipType> types, IReadOnlyList<Person> people)
    {
        ShipTypes = types; People = people;
    }

    /// <summary>구워 둔 표로 만든다(<see cref="AssetPack"/>).</summary>
    public static StartData FromTables(IReadOnlyList<ShipType> types, IReadOnlyList<Person> people) => new(types, people);

    public static StartData Load(string path) => FromBytes(File.ReadAllBytes(path));

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
        return new StartData(types, people);
    }
}
