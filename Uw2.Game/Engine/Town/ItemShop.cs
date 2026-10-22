using System.Text;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Town;

/// <summary>
/// ITEM 가게 「구입 · 매각」 — <c>KOUKAI2.EXE 0x0041FE80 · 0x00420060</c>.
/// </summary>
/// <remarks>
/// <code>
///   아이템 표  SDATA 0x55B6, 20바이트 x 100(메모리 0x004608E0, 시나리오 기억 표 17)
///              +0 이름(15) · +0x10 값 / 100(낱말) · +0x12 힘 · +0x13 갈래(아래 네 비트) · 0x10 은 못 판다
///              갈래 0 ~ 3 칼 · 6 갑옷 · 7 항해 기구 · 8 잡것 · 9 쓰는 것 · A 선물 · B 보석 · C 지도 · D 보물
///   가진 것    표 6 +0x3F ~ +0x52 스물(0x0045BDE7, 0xFF 빈 칸) — 시나리오가 가장 많이 짚는 칸이다
///   파는 것    항구 형편 +0x1D 부터 셋. 하루 안 시각이 6 ~ 8 이면 넷째(+0x20)까지(0x0043CE70)
///   구입       값 x 100. 갈래가 7 아래(칼 · 갑옷)인 것은 같은 것을 또 못 산다(말 0x14C)
///   매각       값 / 2 x 100. 힘이 0 이면 안 받는다(0x3A1). 마다하면 주사위(100) ≤ 운(+0x23)일 때
///              매력(+0x22)% 만큼 올려 다시 부르고, 아니면 거기서 끝이다
/// </code>
/// </remarks>
public static class ItemShop
{
    public const int Table = 17, Size = 20, Count = 100, OwnedFirst = 0x3F, OwnedSlots = 20, ShopField = 0x1D;

    public readonly record struct Item(int Index, string Name, int Price, int Power, int Kind)
    {
        public int Group => Kind & 0x0F;
        public bool Sellable => (Kind & 0x10) == 0 && Power > 0;
    }

    /// <summary>갈래 이름(<c>0x00457598</c>, 9 바이트씩 — EXE 는 일본어라 옮겨 적는다).</summary>
    public static readonly string[] GroupNames = ["검", "자돌검", "곡도", "만도", "방패", "방어무기", "갑옷", "측량기", "항해용품", "소모품"];

    /// <summary>
    /// 풀이 창의 등급(<c>0x0041F54B</c>) — 목록 34(Ｄ · Ｃ · Ｂ · Ａ · ☆)의 차례. 칼붙이(갈래 0 ~ 3)는 공격력 (힘 − 5) / 6(힘 35 이상이면 ☆),
    /// 갑옷(6)은 방어력 (힘 − 10) / 5(힘 40 넘으면 ☆). 그 밖은 등급이 없다(-1).
    /// </summary>
    public static int Grade(Item it) => it.Group switch
    {
        <= 3 => it.Power >= 35 ? 4 : Math.Clamp((it.Power - 5) / 6, 0, 3),
        6 => it.Power > 40 ? 4 : Math.Clamp((it.Power - 10) / 5, 0, 3),
        _ => -1,
    };

    public static Item Get(ScenarioMemory m, int index)
    {
        var raw = new byte[Size];
        for (int i = 0; i < Size; i++) raw[i] = (byte)m.Read(ScenarioMemory.At(Table, index, i), false);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string name = Encoding.GetEncoding(949).GetString(raw, 0, 15).Split('\0')[0].Trim();
        return new Item(index, name, (raw[0x10] | raw[0x11] << 8) * 100, raw[0x12], raw[0x13]);
    }

    public static List<int> Owned(ScenarioMemory m) =>
        [.. Enumerable.Range(0, OwnedSlots).Select(i => m.Read(new ScenarioMemory.Pointer(6, OwnedFirst + i), false))
                      .Where(x => x != 0xFF)];

    /// <summary>이 항구 가게가 파는 것.</summary>
    public static List<int> ForSale(byte[] portStates, int port, int timeOfDay)
    {
        int o = port * Economy.StateSize + ShopField;
        int n = timeOfDay is >= 6 and < 9 ? 4 : 3;
        var list = new List<int>();
        for (int i = 0; i < n && o + i < portStates.Length; i++)
            if (portStates[o + i] != 0xFF) list.Add(portStates[o + i]);
        return list;
    }

    public enum Result { Bought, Owned, Poor, Full }

    public static Result Buy(ScenarioMemory m, int index, ref int gold)
    {
        var item = Get(m, index);
        if (item.Group < 7 && Owned(m).Contains(index)) return Result.Owned;
        if (gold < item.Price) return Result.Poor;
        for (int i = 0; i < OwnedSlots; i++)
        {
            var at = new ScenarioMemory.Pointer(6, OwnedFirst + i);
            if (m.Read(at, false) != 0xFF) continue;
            m.Write(at, index, false);
            gold -= item.Price;
            return Result.Bought;
        }
        return Result.Full;
    }

    /// <summary>값 없이 빈 칸에 넣는다 — 면세증처럼 값을 따로 셈하는 것. 칸이 없으면 거짓.</summary>
    public static bool Give(ScenarioMemory m, int index)
    {
        for (int i = 0; i < OwnedSlots; i++)
        {
            var at = new ScenarioMemory.Pointer(6, OwnedFirst + i);
            if (m.Read(at, false) != 0xFF) continue;
            m.Write(at, index, false);
            return true;
        }
        return false;
    }

    /// <summary>판 값을 처음 부르는 값.</summary>
    public static int Offer(Item item) => item.Price / 100 / 2 * 100;

    /// <summary>마다했을 때 — 운이 받쳐 주면 매력% 올린 값, 아니면 -1(흥정이 깨졌다).</summary>
    public static int Haggle(int offer, int luck, int charm, Func<int, int> rand) =>
        rand(100) <= luck ? offer + offer * charm / 100 : -1;

    /// <summary>장비했는지 — 아이템 +0x13 의 0x10(<c>0x0041F787</c>).</summary>
    public const int Equipped = 0x10, MaxEquipGroup = 6;

    public static bool IsEquipped(ScenarioMemory m, int index) =>
        (m.Read(ScenarioMemory.At(Table, index, 0x13), false) & Equipped) != 0;

    /// <summary>
    /// 「장비」(<c>0x0041F740</c>) — 갈래 0 ~ 6 만 된다. 입고 있으면 벗고, 아니면 같은 갈래의 다른 것을 벗기고 입는다.
    /// 됐으면 지금 입었는지, 안 되면 null.
    /// </summary>
    public static bool? ToggleEquip(ScenarioMemory m, int index)
    {
        var at = ScenarioMemory.At(Table, index, 0x13);
        int kind = m.Read(at, false);
        if ((kind & 0x0F) > MaxEquipGroup) return null;
        if ((kind & Equipped) != 0) { m.Write(at, kind & ~Equipped, false); return false; }
        foreach (int other in Owned(m))
        {
            var o = ScenarioMemory.At(Table, other, 0x13);
            int ok = m.Read(o, false);
            if ((ok & 0x0F) == (kind & 0x0F)) m.Write(o, ok & ~Equipped, false);
        }
        m.Write(at, kind | Equipped, false);
        return true;
    }

    public static void Sell(ScenarioMemory m, int index, int price, ref int gold)
    {
        for (int i = 0; i < OwnedSlots; i++)
        {
            var at = new ScenarioMemory.Pointer(6, OwnedFirst + i);
            if (m.Read(at, false) != index) continue;
            m.Write(at, 0xFF, false);
            gold = (int)Math.Min(PlayerState.MaxGold, (long)gold + price);
            return;
        }
    }
}
