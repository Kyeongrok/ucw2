using System.Text;
using Uw2.Game.Engine.Sea;

namespace Uw2.Game.Engine.Town;

/// <summary>
/// 주점 여급 서른 — 시나리오 기억 표 5(<c>0x0045BB68</c>, <c>SDATA.DAT 0x8BD</c>), 19바이트씩.
/// </summary>
/// <remarks>
/// <code>
///   +0    이름 13     +0x0D 나이     +0x0E 항구     +0x0F 호감(0 ~ 100)
///   +0x12 깃발 — (>> 4) &amp; 3 이 취향: 0 옷 · 악세서리(아이템 갈래 10) · 1 보석(갈래 11) · 2 성의 · 3 무엇이든
/// </code>
/// 「여급과 대화」(목록 13, <c>0x00426910</c>)와 「자랑 · 선물」(목록 14, <c>0x004269A0</c>)이 호감을 올린다.
/// </remarks>
public static class Barmaids
{
    public const int Table = 5, Size = 19, Count = 30;
    public const int Age = 0x0D, Port = 0x0E, Liking = 0x0F, Flags = 0x12, MaxLiking = 100;
    public const int Clothes = 0, Jewels = 1, Sincerity = 2, Anything = 3;

    private static ScenarioMemory.Pointer At(int i, int field) => ScenarioMemory.At(Table, i, field);

    public static int Get(ScenarioMemory m, int i, int field) => m.Read(At(i, field), false);

    public static int InPort(ScenarioMemory m, int port)
    {
        for (int i = 0; i < Count; i++)
            if (Get(m, i, Port) == port && Get(m, i, 0) != 0) return i;
        return -1;
    }

    public static string Name(ScenarioMemory m, int i)
    {
        var raw = new byte[Age];
        for (int k = 0; k < Age; k++) raw[k] = (byte)Get(m, i, k);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949).GetString(raw).Split('\0')[0].Trim();
    }

    public static int Taste(ScenarioMemory m, int i) => Get(m, i, Flags) >> 4 & 3;

    private static int Raise(ScenarioMemory m, int i, int by)
    {
        int before = Get(m, i, Liking);
        m.Write(At(i, Liking), Math.Min(MaxLiking, before + by), false);
        return Get(m, i, Liking) - before;
    }

    /// <summary>
    /// 「그녀의취향」(<c>0x00426840</c>) — rand(50) &lt; 호감이면 취향을 말하고(말 0x195 + 취향) 호감이 (취향 / 2 + 1) x 2 오른다.
    /// 아니면 null(말 0x193 「비밀이예요」).
    /// </summary>
    public static int? AskTaste(ScenarioMemory m, int i, Func<int, int> rand)
    {
        if (rand(50) >= Get(m, i, Liking)) return null;
        int taste = Taste(m, i);
        Raise(m, i, (taste / 2 + 1) * 2);
        return 0x195 + taste;
    }

    /// <summary>「그녀의연령」(<c>0x004268D0</c>) — 호감이 80 을 넘어야 나이를 말한다(말 0x194, 아니면 0x193).</summary>
    public static int? TellAge(ScenarioMemory m, int i) => Get(m, i, Liking) > 80 ? Get(m, i, Age) : null;

    /// <summary>자랑할 수 있는 발견물 — 이번 게임에 있고 찾은 것(<c>0x00425C4C</c>).</summary>
    public static List<int> Boastable(ScenarioMemory m) =>
        [.. Enumerable.Range(0, Discoveries.Count).Where(k => Discoveries.Get(m, k) is { IsAbsent: false, IsFound: true })];

    /// <summary>
    /// 「자랑」(<c>0x00425BE0</c>) — 어려움 / 10 + 1, 취향이 성의 · 무엇이든이면 갈래마다 맞장구(0x13A ~ 0x140)를 치고 두 배.
    /// 호감은 그 항구에 든 뒤 한 번만 오른다(<c>0x0049F5A4</c> 의 0x40). 맞장구 말 번호를 돌려준다(없으면 -1).
    /// </summary>
    public static int Boast(ScenarioMemory m, int i, int discovery, ref bool boasted)
    {
        var d = Discoveries.Get(m, discovery);
        int gain = d.Difficulty / 10 + 1;
        int reply = -1;
        if (Taste(m, i) >= Sincerity)
        {
            reply = BoastReply[d.Kind];
            gain *= 2;
        }
        if (!boasted) { Raise(m, i, gain); boasted = true; }
        return reply;
    }

    /// <summary>갈래마다 맞장구 — 보물 0x13A 「예뻐요」 · 원주민 0x13B 「옷」 · 동물 0x13C 「물리지」 · 식물 0x13D 「꽃」 · 유적 0x13E · 괴물 0x13F · 자연 0x140.</summary>
    public static ReadOnlySpan<short> BoastReply => [0x13A, 0x13B, 0x13C, 0x13D, 0x13E, 0x13F, 0x140, 0x13B];

    /// <summary>줄 수 있는 아이템 — 가진 것 가운데 갈래 10(옷 · 악세서리) · 11(보석)(<c>0x0043CF00</c>).</summary>
    public static List<int> Giftable(ScenarioMemory m) =>
        [.. ItemShop.Owned(m).Where(k => ItemShop.Get(m, k).Group is 10 or 11)];

    /// <summary>
    /// 「선물」(<c>0x00425D70</c>) — 0x141 뒤 값 = min(10, 아이템 힘). 취향이 옷이고 갈래 10(0x142), 보석이고 갈래 11(0x143),
    /// 무엇이든(0x144)이면 두 배이고, 그때 호감이 80 이상이면 입맞춤(0x145). 아이템은 내준다.
    /// </summary>
    /// <returns>잇는 말 번호들</returns>
    public static List<int> Gift(ScenarioMemory m, int i, int item)
    {
        var it = ItemShop.Get(m, item);
        int gain = Math.Min(10, it.Power);
        var lines = new List<int> { 0x141 };
        int taste = Taste(m, i);
        int reply = taste switch
        {
            Clothes when it.Group == 10 => 0x142,
            Jewels when it.Group == 11 => 0x143,
            Anything => 0x144,
            _ => -1,
        };
        if (reply >= 0)
        {
            lines.Add(reply);
            gain *= 2;
            if (Get(m, i, Liking) >= 80) lines.Add(0x145);
        }
        Raise(m, i, gain);
        int none = 0;
        ItemShop.Sell(m, item, 0, ref none);              // 값 없이 내준다
        return lines;
    }
}
