using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Town;

/// <summary>
/// 왕궁 — <c>KOUKAI2.EXE 0x00427E60</c>(목록 16 「알현 · 망명 · 자금원조 · 선박원조 · 밀회」, 손표 0x00427F36).
/// </summary>
/// <remarks>
/// <code>
///   그 항구의 나라   항구 표(22바이트, 0x0045E5E0) +0x15 &amp; 7. 주인공 나라(사람 +0x31 &amp; 7)와 다르면 목록을 막는다(0x00427EB4)
///   자금원조         나라 표 +5 x 1000 을 돈에 더하고 +5 를 0 으로(0x00427A60). 0 이면 말 0x1CD.
///                    더해 6억을 넘으면 받지 않는다(0x212). +5 는 시나리오가 세운다
///   망명             다른 나라 왕궁에서만. 주인공 +0x31 아래 세 비트를 그 나라로, 고향 항구(함대 +0x26)를 이 항구로.
///                    새 나라 표 +2 가 7 이면 6 으로, +5 ~ +7 을 0 으로. 주인공 표 +6 + 나라(나라마다 호의):
///                    옛 나라는 30 이상이면 30 깎고, 새 나라는 +10 해 100 ~ 200 에 둔다(0x00427910)
///   선박원조         나라 표 +8 이 남은 척수, +7 이 줄 선종. 함대 자리와 맡을 동료가 있어야 하고, 한 척 주면 +8 을 하나 줄인다
///                    (0x00427B20 → 0x0043DEA0 — 배 주기 F9 와 같은 손)
/// </code>
/// </remarks>
public static class Palace
{
    public const int NationField = 0x15, FundField = 5, ShipTypeField = 7, ShipField = 8;

    /// <summary>선박원조로 줄 선종 — 남은 것이 없으면 -1.</summary>
    public static int ShipAid(ScenarioMemory m, int nation) =>
        m.Read(ScenarioMemory.At(ScenarioMemory.NationTable, nation, ShipField), false) > 0
            ? m.Read(ScenarioMemory.At(ScenarioMemory.NationTable, nation, ShipTypeField), false) : -1;

    /// <summary>선박원조 한 척을 받았다 — 남은 척수를 하나 줄인다.</summary>
    public static void TookShip(ScenarioMemory m, int nation)
    {
        var at = ScenarioMemory.At(ScenarioMemory.NationTable, nation, ShipField);
        m.Write(at, Math.Max(0, m.Read(at, false) - 1), false);
    }

    /// <summary>그 항구를 다스리는 나라 — 항구 표 나라 바이트(0x0043AED0 +0x15)의 아래 세 비트.</summary>
    public static int NationOf(PortTable.Port port) => port.Nation & 7;

    /// <summary>주인공 표 +6 + 나라 — 그 나라의 호의.</summary>
    public const int FavorFirst = 6;

    /// <summary>망명한다 — 사람 표 · 나라 표 · 주인공 표를 고친다. 고향 항구는 부르는 쪽이 옮긴다.</summary>
    public static void Defect(ScenarioMemory m, int hero, int newNation)
    {
        int kind = Navigators.Get(m, hero, Navigators.Kind);
        int oldNation = kind & 7;
        var oldFavor = ScenarioMemory.At(ScenarioMemory.HeroTable, hero, FavorFirst + oldNation);
        var newFavor = ScenarioMemory.At(ScenarioMemory.HeroTable, hero, FavorFirst + newNation);
        Navigators.Set(m, hero, Navigators.Kind, (kind & 0xF0) | (newNation & 7));
        var two = ScenarioMemory.At(ScenarioMemory.NationTable, newNation, 2);
        if (m.Read(two, false) == 7) m.Write(two, 6, false);
        for (int f = 5; f <= 7; f++) m.Write(ScenarioMemory.At(ScenarioMemory.NationTable, newNation, f), 0, false);
        int old = m.Read(oldFavor, false);
        m.Write(oldFavor, old >= 30 ? old - 30 : old, false);
        m.Write(newFavor, Math.Clamp(m.Read(newFavor, false) + 10, 100, 200), false);
    }

    /// <summary>자금원조를 받는다 — 받은 돈(없으면 0, 넘쳐 못 받으면 -1).</summary>
    public static int TakeFunds(ScenarioMemory m, int nation, ref int gold)
    {
        var at = ScenarioMemory.At(ScenarioMemory.NationTable, nation, FundField);
        int amount = m.Read(at, false) * 1000;
        if (amount == 0) return 0;
        if ((long)gold + amount > PlayerState.MaxGold) return -1;
        gold += amount;
        m.Write(at, 0, false);
        return amount;
    }
}
