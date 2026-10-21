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
    public const int Employed = 6, Captain = 2, FreeFrom = 3, MinLikingToHire = 30;

    /// <summary>배를 맡길 수 있는 동료 — 처지가 3 이상인 첫 동료(<c>0x00429180</c>). 없으면 -1.</summary>
    public static int FreeMate(ScenarioMemory m) =>
        Mates(m).Select(x => x.Person).FirstOrDefault(p => Get(m, p, Status) >= FreeFrom, -1);

    /// <summary>
    /// 「인사변경」(<c>0x00445ED0</c>) — 처지 3 부선장 · 4 경리주임 · 5 수석항해사(<c>0x00445BE0</c>). 자리마다 한 사람이다.
    /// </summary>
    public static readonly string[] PostNames = ["부선장", "경리주임", "수석항해사"];

    public const int FirstPost = 3;

    /// <summary>그 자리에 있는 동료 — 없으면 -1.</summary>
    public static int InPost(ScenarioMemory m, int post) =>
        Mates(m).Select(x => x.Person).FirstOrDefault(p => Get(m, p, Status) == FirstPost + post, -1);

    /// <summary>
    /// 동료를 자리에 앉힌다. 선장이면 안 된다(거짓, 말 0xE3). 그 자리에 있던 이는 쉬고(6), 앉힌 이가 있던 자리는 빈다.
    /// </summary>
    public static bool Appoint(ScenarioMemory m, int person, int post)
    {
        if (Get(m, person, Status) == Captain) return false;
        int old = InPost(m, post);
        if (old >= 0) Set(m, old, Status, Employed);
        Set(m, person, Status, FirstPost + post);
        return true;
    }

    /// <summary>배를 맡긴다 — 처지 2(<c>0x00429C04</c>). 배에서 내리면 6 으로 돌린다.</summary>
    public static void Command(ScenarioMemory m, int person, bool aboard) => Set(m, person, Status, aboard ? Captain : Employed);

    /// <summary>한턱내기 값 — 항구 형편 +0x24 갈래마다(<c>0x004571E8</c>).</summary>
    public static ReadOnlySpan<byte> TreatPrice => [3, 4, 5, 6, 3, 3, 2, 2, 1, 1, 3, 2, 3, 2, 0, 0];

    /// <summary>
    /// 새 사람 지어내기(<c>0x0042AE30</c>) — 이름 표(26 바이트, 나라마다 열여섯)에서 이름 · 성을 rand(16) 로 따로 골라 사람 백스물 가운데
    /// 같은 이름 · 성이 없을 때까지 다시 고른다. 나이(+0x2A) rand(16) + 20, 능력치 여덟 rand(35) + 60, 항해 · 전투 레벨은
    /// max(그 칸의 옛 값 + rand(4) + 2, rand(5) + 8)(100 까지), 경험 둘은 0, +0x2F = rand(16) &lt;&lt; 4 | <paramref name="mood"/>,
    /// 기능 +0x30 = rand(16), +0x31 = 나라 | 0x20, 얼굴 +0x1A = rand(0x3FFF) | 0xC000.
    /// 해적(나라 6)은 이름 표가 없어(96 줄 너머) 여섯 나라 가운데 하나로 고른다 — 원본은 그 자리를 읽다 실패한다.
    /// </summary>
    public static void Forge(ScenarioMemory m, int person, int nation, int mood, byte[] names, Func<int, int> rand)
    {
        const int Rec = 26;
        int rows = names.Length / Rec;
        string Read(int off) => System.Text.Encoding.GetEncoding(949).GetString(names, off, 13).Split('\0')[0];
        int band = nation is >= 0 and < 6 ? nation : rand(6);
        byte[] name = new byte[13], family = new byte[13];
        for (int guard = 0; guard < 200; guard++)
        {
            int a = Math.Min(rows - 1, rand(16) + band * 16), b = Math.Min(rows - 1, rand(16) + band * 16);
            Array.Copy(names, a * Rec, name, 0, 13);
            Array.Copy(names, b * Rec + 13, family, 0, 13);
            string n = Read(a * Rec), f = Read(b * Rec + 13);
            bool taken = false;
            for (int p = 0; p < People && !taken; p++)
            {
                if (p == person) continue;
                var pn = new byte[26];
                for (int i = 0; i < 26; i++) pn[i] = (byte)Get(m, p, i);
                var cp = System.Text.Encoding.GetEncoding(949);
                taken = cp.GetString(pn, 0, 13).Split('\0')[0] == n && cp.GetString(pn, 13, 13).Split('\0')[0] == f;
            }
            if (!taken) break;
        }
        for (int i = 0; i < 13; i++) { Set(m, person, i, name[i]); Set(m, person, 13 + i, family[i]); }
        Set(m, person, 0x2A, rand(16) + 20);
        for (int k = 0; k < 8; k++) Set(m, person, Stats + k, rand(35) + 60);
        foreach (int f in (int[])[NavLevel, BattleLevel])
            Set(m, person, f, Math.Min(100, Math.Max(Get(m, person, f) + rand(4) + 2, rand(5) + 8)));
        for (int f = 0x26; f < 0x2A; f++) Set(m, person, f, 0);
        Set(m, person, Mood, rand(16) << 4 | mood);
        Set(m, person, 0x30, rand(16));
        Set(m, person, Kind, nation | 0x20);
        int face = rand(0x3FFF) | 0xC000;
        Set(m, person, 0x1A, face & 0xFF);
        Set(m, person, 0x1B, face >> 8);
    }

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
    /// 0x20 이 없는 빈 칸은 셋에 하나 꼴로 새 사람을 지어(<see cref="Forge"/>, 나라 rand(6) · 기분 rand(3)) 항구 rand(42) 에 둔다.
    /// </summary>
    public static void Wander(ScenarioMemory m, Func<int, int> rand, byte[]? names = null)
    {
        for (int p = 6; p < People; p++)
        {
            int kind = Get(m, p, Kind);
            if ((kind & 0x20) == 0)
            {
                // 빈 칸 — 셋에 하나 꼴로 새 사람을 지어 어느 항구에 둔다(0x0042C1F3).
                if (names == null || rand(3) != 0) continue;
                int mood = rand(3);
                Forge(m, p, rand(6), mood, names, rand);
                Set(m, p, Port, rand(42));
                Set(m, p, Fleet, 0xFF);
                continue;
            }
            if ((kind & 0x40) != 0 || Get(m, p, Fleet) != 0xFF || Get(m, p, Port) == 0xFF) continue;
            if (rand(3) != 0) continue;
            Set(m, p, Port, Math.Clamp(rand(5) + Get(m, p, Port) - 2, 0, 0x60));
        }
    }

    /// <summary>
    /// 맞수 따라잡기(<c>0x0042C240</c>) — 다른 주인공 다섯(사람 0 ~ 5)과 일곱째 몫마다, 주인공 항해 레벨이 더 높으면 그 차 + rand(2) + 1 만큼,
    /// 전투 레벨이 더 높으면 차 / 2 + rand(2) + 1 만큼 올리고, 오른 만큼 능력치를 올린다(<c>0x0042B640</c>: 통솔 +rand(3), 항해면 항해술 · 지식 · 직감,
    /// 전투면 용기 · 검술 +rand(5), 100 까지). 일곱째는 레벨을 사람 6 에서 읽고 쓰지만 능력치는 사람 60 에 올린다 — 원본 그대로.
    /// </summary>
    public static void KeepUp(ScenarioMemory m, int hero, Func<int, int> rand)
    {
        for (int r = 0; r < 7; r++)
        {
            int statsOf = r == 6 ? 60 : r;
            if (statsOf == hero) continue;
            int heroNav = Get(m, hero, NavLevel), heroBat = Get(m, hero, BattleLevel);
            int nav = Get(m, r, NavLevel);
            if (heroNav > nav)
            {
                int ups = rand(2) + heroNav - nav + 1;
                Set(m, r, NavLevel, (nav + ups) & 0xFF);
                Train(statsOf, ups, battle: false);
            }
            int bat = Get(m, r, BattleLevel);
            if (heroBat > bat)
            {
                int ups = rand(2) + (heroBat - bat) / 2 + 1;
                Set(m, r, BattleLevel, (bat + ups) & 0xFF);
                Train(statsOf, ups, battle: true);
            }
        }

        void Train(int p, int times, bool battle)
        {
            for (int i = 0; i < times; i++)
            {
                Set(m, p, Stats, Math.Min(100, Get(m, p, Stats) + rand(3)));
                foreach (int k in battle ? (int[])[4, 5] : [1, 2, 3])
                    Set(m, p, Stats + k, Math.Min(100, Get(m, p, Stats + k) + rand(5)));
            }
        }
    }

    /// <summary>
    /// 달마다 급여(<c>0x0042C4E0</c>) — 동료 급여를 다 더해 돈에서 낸다. 모자라면 내지 않고 모든 동료의 +0x31 에 0x80 을 세운다.
    /// 그다음 호감이 100 이 아닌 동료마다 <c>호감 += 주인공 능력치 +0x1C + 급여 / 10 x 2 − 그 이의 +0x1C ~ +0x23 가운데 가장 큰 것</c>
    /// (0~100). 낸 급여 합(못 냈으면 음수)을 돌려준다.
    /// </summary>
    public static int PayWages(ScenarioMemory m, int hero, ref int gold)
    {
        var mates = Mates(m).ToList();
        int total = mates.Sum(x => x.Wage);
        bool paid = total == 0 || gold >= total;
        if (paid) gold -= total;
        else
            foreach (var x in mates) Set(m, x.Person, Kind, Get(m, x.Person, Kind) | 0x80);
        int lead = Get(m, hero, Stats);
        foreach (var x in mates)
        {
            int liking = Get(m, x.Person, Liking);
            if (liking == 100) continue;
            int best = Enumerable.Range(0, 8).Max(k => Get(m, x.Person, Stats + k));
            Set(m, x.Person, Liking, Math.Clamp(liking + lead + x.Wage / 10 * 2 - best, 0, 100));
        }
        return paid ? total : -total;
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

    /// <summary>
    /// 바다 「급여설정」(<c>0x00441B50</c>) — 호감 80 이상이면 걱정 말라 하고(말 0xDC), 지금보다 적으면 마다한다(말 0xE0 「말도 안돼」).
    /// 아니면 호감 += (새 급여 − 지금) / 5, 100 까지, 급여 / 10 을 적는다(말 0xDF). 급여는 2000 까지다.
    /// </summary>
    public const int MaxWage = 2000;

    public enum WageAnswer { NoNeed, TooLow, Thanks }

    public static WageAnswer SetWage(ScenarioMemory m, int slot, int person, int wage)
    {
        if (Get(m, person, Liking) >= 80) return WageAnswer.NoNeed;
        var at = new ScenarioMemory.Pointer(MateTable, WageFirst + slot);
        int now = m.Read(at, false) * 10;
        wage = Math.Min(wage, MaxWage);
        if (wage < now) return WageAnswer.TooLow;
        Set(m, person, Liking, Math.Min(100, Get(m, person, Liking) + (wage - now) / 5));
        m.Write(at, wage / 10, false);
        return WageAnswer.Thanks;
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

    /// <summary>항해 경험(<c>0x0041A4C0</c> 에 +0x24 레벨 · +0x26 경험 낱말을 넘긴다).</summary>
    public const int NavExp = 0x26;

    /// <summary>
    /// 항해 경험을 더한다(<c>0x0041A4C0</c>) — 경험이 min(레벨, 18)² x 30 을 넘을 때마다 레벨이 오른다(100 이면 경험 0). 한 번이라도 오르면
    /// 능력치 0 ~ 3 에 min(rand(3), 100 − 값). 이어 알리는 손(<c>0x0041A7C0</c>)이 능력치 0 ~ 2 에 한 번 더 min(rand(3), 100 − 값)을 더하고
    /// 그 이름들을 적는다. 오른 레벨 수와 그 둘째로 오른 능력치 번호들을 돌려준다.
    /// </summary>
    public static (int Ups, List<int> Raised) SailExperience(ScenarioMemory m, int person, int amount, Func<int, int> rand)
    {
        var at = ScenarioMemory.At(PersonTable, person, NavExp);
        int exp = (short)(m.Read(at, true) + amount), level = Get(m, person, NavLevel), ups = 0;
        static int Need(int l) { int k = Math.Min(l, 18); return k * k * 30; }
        while (exp >= Need(level))
        {
            if (level == 100) { exp = 0; break; }
            exp -= Need(level);
            level++;
            ups++;
        }
        m.Write(at, exp & 0xFFFF, true);
        Set(m, person, NavLevel, level);
        var raised = new List<int>();
        if (ups == 0) return (0, raised);
        for (int k = 0; k < 4; k++) Set(m, person, Stats + k, Get(m, person, Stats + k) + Math.Min(rand(3), 100 - Get(m, person, Stats + k)));
        for (int k = 0; k < 3; k++)
        {
            int up = Math.Min(rand(3), 100 - Get(m, person, Stats + k));
            if (up <= 0) continue;
            Set(m, person, Stats + k, Get(m, person, Stats + k) + up);
            raised.Add(k);
        }
        return (ups, raised);
    }

    /// <summary>
    /// 동료가 떠난다(<c>0x00412ED0</c>) — 동료 칸에서 지우고, 처지 0 · 함대 없음 · 항구 rand(0x36) + 3 으로 둔다. 배는 부르는 쪽이 뺀다.
    /// </summary>
    public static void Leave(ScenarioMemory m, int person, Func<int, int> rand)
    {
        for (int i = 0; i < MateSlots; i++)
            if (m.Read(new ScenarioMemory.Pointer(MateTable, MateFirst + i), false) == person)
                m.Write(new ScenarioMemory.Pointer(MateTable, MateFirst + i), 0xFF, false);
        Set(m, person, Status, 0);
        Set(m, person, Fleet, 0xFF);
        Set(m, person, Port, rand(0x36) + 3);
    }
}
