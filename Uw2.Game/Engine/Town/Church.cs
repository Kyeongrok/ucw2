namespace Uw2.Game.Engine.Town;

/// <summary>
/// 교회 「예배 · 기부」 — <c>KOUKAI2.EXE 0x004217D0 · 0x00421830</c>. 주인공 사람 기록 +0x23(운)을 올린다.
/// </summary>
/// <remarks>
/// <code>
///   예배   들어온 뒤 처음이면(0x0049A188) 운 += 주사위(2), 100 까지
///   기부   몫 = 돈 / 낸 돈. 몫이 10 을 넘으면(가진 돈의 10분의 1 도 안 내면) 받기만 한다(말 0x5E).
///          아니면(0x5F) 낸 돈이 (주사위(5) + 1) x 100 이상일 때 운 += 11 − 몫, 100 까지
/// </code>
/// </remarks>
public static class Church
{
    public const int Luck = 0x23;

    public static int Worship(ScenarioMemory m, int hero, Func<int, int> rand)
    {
        int before = Navigators.Get(m, hero, Luck);
        Navigators.Set(m, hero, Luck, Math.Min(100, before + rand(2)));
        return Navigators.Get(m, hero, Luck) - before;
    }

    /// <summary>기부한다 — 돈은 부르는 쪽이 뺀다. 오른 운을 돌려준다(-1 은 너무 적어 셈하지 않은 것).</summary>
    public static int Donate(ScenarioMemory m, int hero, int gold, int amount, Func<int, int> rand)
    {
        if (amount <= 0) return 0;
        int ratio = gold / amount;
        if (ratio > 10) return -1;
        if (amount < (rand(5) + 1) * 100) return 0;
        int before = Navigators.Get(m, hero, Luck);
        Navigators.Set(m, hero, Luck, Math.Clamp(before - ratio + 11, 0, 100));
        return Navigators.Get(m, hero, Luck) - before;
    }
}
