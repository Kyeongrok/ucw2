namespace Uw2.Game.Engine.Town;

/// <summary>
/// 주점 · 여관의 항해자 — 찾기 · 한턱내기 · 고용(<c>KOUKAI2.EXE 0x0043CB10 · 0x00425260 · 0x00425590</c>).
/// 사람 표(시나리오 기억 표 3)와 동료 칸(표 6)을 그대로 읽고 쓴다.
/// </summary>
/// <remarks>
/// <code>
///   사람 +0x1C ~ +0x22  능력치 일곱   +0x24 · +0x25 레벨 둘   +0x2B 호감(0~100)
///        +0x2C 탄 함대(0xFF 없음)   +0x2D 머무는 항구(0xFF 없음)   +0x2E 처지(0 떠돌이 · 1 제독 · 6 우리 동료)
///        +0x2F 위 네 비트는 성격(0x0042AFEC 가 주사위로 만든다) — 0x10 이 서면 주점, 아니면 여관에서 만난다.
///              아래 두 비트 기질, 비트 0x40 술을 좋아한다   +0x31 비트 0x20 항해자
///   표 6 +3 ~ +0x20  동료 서른 칸(사람 번호, 0xFF 빈 칸)   +0x21 ~ +0x3E  저마다 급여 / 10
///
///   이 항구의 이    주인공이 아니고 +0x31 &amp; 0x20, 그리고 +0x2D 가 이 항구이거나 우리 함대(+0x2C)에 탄 이(0x0043CB10).
///                    주점은 그 가운데 +0x2F &amp; 0x10 (0x0043CBB0), 여관은 나머지(0x0043CC20 ← 0x0041F070)
///   한턱내기         값은 항구 형편 +0x24 갈래의 표(0x004571E8) — 1~6. 호감 += 6 x (기질이 같으면 3, 아니면 1) x (술꾼이면 2)
///   고용             이미 매인 이(+0x2E ≠ 0) · 빈 칸 없음 · 호감 30 이하면 못 한다(호감은 주점만 — 여관은 0x00426F50 이
///                    0x00425590(0) 을 불러 보지 않는다). 그 이의 가장 높은 능력치 k 로
///                    우리 = (레벨 합) x 능력치[k] x (1 + 작위 / 10), 그 이 = (레벨 합) x 능력치[k].
///                    우리 &gt; 주사위(20) + 그 이 이면 받아 준다. 급여 = min(20, 그 이 / 400 + 주사위(3) + 1) x 10
///   들어오면         +0x2D = 0xFF, +0x2C = 우리 함대, +0x2E = 6, 호감 += 10, 빈 칸에 이름과 급여 / 10
/// </code>
/// </remarks>
public static class Navigators
{
    public const int People = 120, PersonTable = ScenarioMemory.PeopleTable, MateTable = 6;
    public const int MateFirst = 3, MateSlots = 30, WageFirst = MateFirst + MateSlots;
    public const int Stats = 0x1C, NavLevel = 0x24, BattleLevel = 0x25, Liking = 0x2B, Fleet = 0x2C, Port = 0x2D, Status = 0x2E,
                     Mood = 0x2F, Kind = 0x31;
    public const int Employed = 6, MinLikingToHire = 30;

    /// <summary>한턱내기 값 — 항구 형편 +0x24 갈래마다(<c>0x004571E8</c>).</summary>
    public static ReadOnlySpan<byte> TreatPrice => [3, 4, 5, 6, 3, 3, 2, 2, 1, 1, 3, 2, 3, 2, 0, 0];

    public static int Get(ScenarioMemory m, int person, int field) =>
        m.Read(ScenarioMemory.At(PersonTable, person, field), false);

    public static void Set(ScenarioMemory m, int person, int field, int v) =>
        m.Write(ScenarioMemory.At(PersonTable, person, field), v, false);

    /// <summary>지금 이 항구 주점(<paramref name="pub"/>) 또는 여관에 있는 항해자들.</summary>
    public static List<int> InTown(ScenarioMemory m, int port, int hero, bool pub = true)
    {
        int heroFleet = Get(m, hero, Fleet);
        var list = new List<int>();
        for (int p = 0; p < People; p++)
        {
            if (p == hero || (Get(m, p, Kind) & 0x20) == 0) continue;
            bool here = Get(m, p, Port) == port || (heroFleet != 0xFF && Get(m, p, Fleet) == heroFleet);
            if (here && ((Get(m, p, Mood) & 0x10) != 0) == pub) list.Add(p);
        }
        return list;
    }

    /// <summary>
    /// 달이 바뀔 때 떠돌이 항해자가 옮겨 간다(<c>0x0042C190</c>) — 사람 6 부터, +0x31 &amp; 0x20 이고 0x40 이 아니며
    /// 함대에 안 타고(+0x2C 0xFF) 머무는 항구가 있는 이는 1/3 로 항구 번호가 주사위(5) − 2 만큼(0~96) 움직인다.
    /// 항해자가 아닌 이를 새로 빚는 갈래(0x0042AE30)는 아직이다.
    /// </summary>
    public static void Wander(ScenarioMemory m, Func<int, int> rand)
    {
        for (int p = 6; p < People; p++)
        {
            int kind = Get(m, p, Kind);
            if ((kind & 0x20) == 0 || (kind & 0x40) != 0 || Get(m, p, Fleet) != 0xFF || Get(m, p, Port) == 0xFF) continue;
            if (rand(3) != 0) continue;
            Set(m, p, Port, Math.Clamp(rand(5) + Get(m, p, Port) - 2, 0, 0x60));
        }
    }

    /// <summary>동료 칸 — (칸, 사람, 급여).</summary>
    public static IEnumerable<(int Slot, int Person, int Wage)> Mates(ScenarioMemory m)
    {
        for (int i = 0; i < MateSlots; i++)
        {
            int p = m.Read(new ScenarioMemory.Pointer(MateTable, MateFirst + i), false);
            if (p != 0xFF) yield return (i, p, m.Read(new ScenarioMemory.Pointer(MateTable, WageFirst + i), false) * 10);
        }
    }

    /// <summary>한턱낸다 — 오른 호감을 돌려준다.</summary>
    public static int Treat(ScenarioMemory m, int navigator, int hero)
    {
        int mood = Get(m, navigator, Mood);
        int gain = 6 * (((mood ^ Get(m, hero, Mood)) & 3) == 0 ? 3 : 1) * ((mood & 0x40) != 0 ? 2 : 1);
        int before = Get(m, navigator, Liking);
        Set(m, navigator, Liking, Math.Min(100, before + gain));
        return Get(m, navigator, Liking) - before;
    }

    public enum Answer { Hired, AlreadyBound, NoRoom, NotFriendly, Refused }

    /// <summary>고용을 청한다 — 받아 주면 급여(10 의 배수)를 <paramref name="wage"/> 로 준다. 아직 들이지는 않는다.</summary>
    public static Answer Ask(ScenarioMemory m, int navigator, int hero, int rank, Func<int, int> rand, out int wage,
                             bool needLiking = true)
    {
        wage = 0;
        if (Get(m, navigator, Status) != 0) return Answer.AlreadyBound;
        if (Mates(m).Count() >= MateSlots) return Answer.NoRoom;
        if (needLiking && Get(m, navigator, Liking) <= MinLikingToHire) return Answer.NotFriendly;
        int best = 0;
        for (int k = 1; k < 7; k++)
            if (Get(m, navigator, Stats + k) > Get(m, navigator, Stats + best)) best = k;
        int theirs = (Get(m, navigator, NavLevel) + Get(m, navigator, BattleLevel)) * Get(m, navigator, Stats + best);
        int ours = (Get(m, hero, NavLevel) + Get(m, hero, BattleLevel)) * Get(m, hero, Stats + best);
        ours += ours * rank / 10;
        if (ours <= rand(20) + theirs) return Answer.Refused;
        wage = Math.Min(20, theirs / 400 + rand(3) + 1) * 10;
        return Answer.Hired;
    }

    /// <summary>들인다 — 빈 칸에 넣고 처지 · 함대 · 호감을 고친다(<c>0x00425806</c>).</summary>
    public static bool Join(ScenarioMemory m, int navigator, int hero, int wage)
    {
        int slot = -1;
        for (int i = 0; i < MateSlots && slot < 0; i++)
            if (m.Read(new ScenarioMemory.Pointer(MateTable, MateFirst + i), false) == 0xFF) slot = i;
        if (slot < 0) return false;
        Set(m, navigator, Port, 0xFF);
        Set(m, navigator, Fleet, Get(m, hero, Fleet));
        Set(m, navigator, Status, Employed);
        Set(m, navigator, Liking, Math.Min(100, Get(m, navigator, Liking) + 10));
        m.Write(new ScenarioMemory.Pointer(MateTable, MateFirst + slot), navigator, false);
        m.Write(new ScenarioMemory.Pointer(MateTable, WageFirst + slot), wage / 10, false);
        return true;
    }
}
