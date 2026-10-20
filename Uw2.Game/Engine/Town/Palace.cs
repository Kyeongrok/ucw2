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
///   선박원조         나라 표 +8 — 아직
/// </code>
/// </remarks>
public static class Palace
{
    public const int NationField = 0x15, FundField = 5, ShipField = 8;

    /// <summary>그 항구를 다스리는 나라 — 항구 표 나라 바이트(0x0043AED0 +0x15)의 아래 세 비트.</summary>
    public static int NationOf(PortTable.Port port) => port.Nation & 7;

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
