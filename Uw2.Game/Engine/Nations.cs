namespace Uw2.Game.Engine;

/// <summary>
/// 나라 셈 — 달마다(<c>KOUKAI2.EXE 0x0042B960</c>) 나라 표(<c>0x0045E268</c>, 32 바이트 x 7, 시나리오 기억 표 0)를 고친다.
/// </summary>
/// <remarks>
/// 나라 표 자리.
/// <code>
///   +0     낱말  나라 세기(경제 합)          +2  노리는 나라(7 은 주인공)       +3  방침 0 ~ 8
///   +4     상선 행선 항구                      +9  호전성                          +0x0A 본거지 항구
///   +0x0B  나라마다 사이(일곱, 100 이 제 나라)  +0x12 나라마다 맞섬(여섯)
/// </code>
/// 여기 옮긴 것은 세기(<c>0x0042B980</c>) · 노리는 나라(<c>0x0042BB60</c>) · 방침(<c>0x0042BCB0</c>) · 상선 행선(<c>0x0042BD90</c>)이다.
/// 나라 사이 형편(<c>0x0042BEA0</c>) · 원조 쌓기(<c>0x0042B540</c>)도 옮겼다. 함대 다시 짓기(<c>0x0042ACF0</c> ~ <c>0x0042B3E0</c>)는 아직이다.
/// </remarks>
public static class Nations
{
    public const int Count = 7, Pirates = 6, HeroTarget = 7;
    public const int PowerField = 0, TargetField = 2, PolicyField = 3, PortField = 4, AggressionField = 9, RelationField = 0x0B;

    /// <summary>바다 갈래(항구 형편 +0x1C)마다의 나눗수(<c>0x00458DC8</c>).</summary>
    public static ReadOnlySpan<byte> RegionDivisor => [4, 3, 3, 2, 3, 1, 1, 1];

    /// <summary>
    /// 나라 세기(<c>0x0042B980</c>) — 항구마다 임자 나라(항구 표 +0x15 &amp; 7)와 바다 갈래(항구 형편 +0x1C)로 공업(+4) 합과
    /// 경제(+0, 항구 표 +0x15 의 비트 8 이면 두 배) 합을 모은다. 나라마다 세기 = 갈래 0 경제 / 4
    /// + 갈래 4 ~ 7 가운데 공업 합이 300 넘는 곳의 경제 / 나눗수 + 갈래 1 · 2 를 차례로(공업 300 아래에서 멈춘다)
    /// + (갈래 2 나 4 의 공업이 300 넘고 갈래 3 도 넘으면) 갈래 3 의 경제 / 2. 해적 세기는 여섯 나라의 평균.
    /// </summary>
    public static int[] Powers(IReadOnlyList<(int Owner, bool Major, int Economy, int Industry, int Region)> ports)
    {
        long[,] industry = new long[Count, 8], economy = new long[Count, 8];
        foreach (var p in ports)
        {
            if (p.Owner < 0 || p.Owner >= Count) continue;
            int r = p.Region & 7;
            industry[p.Owner, r] += p.Industry;
            economy[p.Owner, r] += p.Economy * (p.Major ? 2 : 1);
        }
        var power = new int[Count];
        int sum = 0;
        for (int n = 0; n < 6; n++)
        {
            long total = economy[n, 0] / RegionDivisor[0];
            for (int r = 4; r < 8; r++)
                if (industry[n, r] >= 300) total += economy[n, r] / RegionDivisor[r];
            for (int r = 1; r < 3; r++)
            {
                if (industry[n, r] < 300) break;
                total += economy[n, r] / RegionDivisor[r];
            }
            if ((industry[n, 2] >= 300 || industry[n, 4] >= 300) && industry[n, 3] >= 300)
                total += economy[n, 3] / RegionDivisor[3];
            power[n] = (short)total;
            sum += power[n];
        }
        power[Pirates] = sum / 6;
        return power;
    }

    /// <summary>
    /// 노리는 나라(<c>0x0042BB60</c>) — 여섯 나라는 저마다 (그 나라 세기 x 사이 / 100) 가 가장 작은 다른 나라를,
    /// 해적은 (100 − 사이) x 세기가 가장 큰 나라를 고른다. 그러고도 |200 − 주인공 호감(주인공 표 +6 + 나라)| 이
    /// rand(180 − 호전성) + 100(해적은 rand(50) + 100)보다 크면 주인공을 노린다(7).
    /// </summary>
    public static int Target(int nation, Func<int, int, int> read, int heroFavour, Func<int, int> rand)
    {
        int best;
        if (nation < Pirates)
        {
            best = HeroTarget;
            int low = 0xFFFF;
            for (int i = 0; i < Count; i++)
            {
                if (i == nation) continue;
                int v = read(i, PowerField) * read(nation, RelationField + i) / 100;
                if (v < low) { low = v; best = i; }
            }
            return Math.Abs(200 - heroFavour) > rand(180 - read(nation, AggressionField)) + 100 ? HeroTarget : best;
        }
        best = 0;
        int high = 0;
        for (int i = 0; i < 6; i++)
        {
            int v = (100 - read(nation, RelationField + i)) * read(i, PowerField);
            if (v > high) { high = v; best = i; }
        }
        return Math.Abs(200 - heroFavour) > rand(50) + 100 ? HeroTarget : best;
    }

    /// <summary>
    /// 방침(<c>0x0042BCB0</c>) — 노리는 나라 tn(7 이면 주인공 나라) 보다 약하거나 같으면 rand(120), 세면 rand(90) 이 호전성보다 작을 때
    /// 싸움 방침 — tn 이 더 세면 4, 아니면 5(<c>0x0042BD40</c>. 주인공이면 3, 해적이면 6 을 먼저 적지만 그 뒤에 덮어쓴다 — 원본 그대로).
    /// 아니면 rand(3)(0 ~ 2 의 장사 방침). 해적은 주인공을 노리면 7, 아니면 8.
    /// </summary>
    public static int Policy(int nation, Func<int, int, int> read, int heroNation, Func<int, int> rand)
    {
        int target = read(nation, TargetField);
        if (nation == Pirates) return target != HeroTarget ? 8 : 7;
        int tn = target >= HeroTarget ? heroNation : target;
        int mine = (short)read(nation, PowerField), theirs = tn is >= 0 and < Count ? (short)read(tn, PowerField) : 0;
        int roll = rand(mine <= theirs ? 120 : 90);
        if (roll >= read(nation, AggressionField)) return rand(3);
        return theirs > mine ? 4 : 5;
    }

    /// <summary>
    /// 상선 행선(<c>0x0042BD90</c>) — 방침마다의 고르개(<c>0x00458DD0</c>)를 항구 0 ~ 99 에 차례로 대어, 맞는 항구마다 다섯에 하나로 고른다.
    /// 한 바퀴에 못 고르면 다시 돈다. 맞는 항구가 하나도 없으면 고르개를 바꿔(<c>0x0042BE84</c>: 0 → 1, 1 → 2, 3 · 4 · 5 → 0, 6 → 1) 다시.
    /// <code>
    ///   0 · 6  임자 없는 항구(6)          1  제 나라 항구(본거지 말고)      2  항구 75 ~ 92
    ///   3      주인공 나라 항구(본거지 말고)  4 · 5  노리는 나라 항구(본거지 말고, 7 이면 주인공 나라)
    /// </code>
    /// </summary>
    public static int MerchantPort(int nation, Func<int, int, int> read, Func<int, int> portOwner, int heroNation, Func<int, int> rand, int portCount = 100)
    {
        int policy = read(nation, PolicyField);
        int Capital(int n) => n is >= 0 and < Count ? read(n, 0x0A) : -1;
        bool Fits(int rule, int p)
        {
            int owner = portOwner(p) & 7;
            switch (rule)
            {
                case 0 or 6: return owner == Pirates;
                case 1: return owner == nation && p != Capital(nation);
                case 2: return p is >= 75 and <= 92;
                case 3: return owner == heroNation && p != Capital(heroNation);
                case 4 or 5:
                    int tn = read(nation, TargetField);
                    if (tn == HeroTarget) tn = heroNation;
                    return owner == tn && p != Capital(tn);
                default: return false;
            }
        }
        for (int guard = 0; guard < 64; guard++)
        {
            bool any = false;
            for (int p = 0; p < portCount; p++)
            {
                if (!Fits(policy, p)) continue;
                any = true;
                if (rand(5) == 0) return p;
            }
            if (!any)
                policy = policy switch { 0 or 6 => 1, 1 => 2, 3 or 4 or 5 => 0, _ => policy };
            if (!any && policy == 2 && portCount <= 75) return read(nation, PortField);
        }
        return read(nation, PortField);
    }

    /// <summary>
    /// 원조 쌓기(<c>0x0042B540</c>) — 여섯 나라마다 주인공 호감(주인공 표 +6 + 나라)이 130 이상이면 k = 작위(+0x0D) x 나라 세기로
    /// 나라 +5(자금원조, x 1000 닢) += k / 3000, +6 += k / 1000 (둘 다 100 까지), +7(원조 선종) = min(7, k / 4000), +8(원조 척수) += min(10, k / 4000).
    /// 호감이 70 아래면 넷 다 거둔다(+7 은 0xFF).
    /// </summary>
    public static void Aid(ScenarioMemory m, int hero)
    {
        int rank = m.Read(ScenarioMemory.At(ScenarioMemory.HeroTable, hero, ScenarioMemory.RankField), false);
        for (int n = 0; n < 6; n++)
        {
            ScenarioMemory.Pointer At(int f) => ScenarioMemory.At(ScenarioMemory.NationTable, n, f);
            int favour = m.Read(ScenarioMemory.At(ScenarioMemory.HeroTable, hero, 6 + n), false);
            if (favour >= 130)
            {
                int k = rank * (short)m.Read(At(PowerField), true);
                m.Write(At(5), Math.Min(100, (m.Read(At(5), false) + k / 3000) & 0xFF), false);
                m.Write(At(6), Math.Min(100, (m.Read(At(6), false) + k / 1000) & 0xFF), false);
                m.Write(At(7), Math.Min(7, k / 4000), false);
                m.Write(At(8), (m.Read(At(8), false) + Math.Min(10, k / 4000)) & 0xFF, false);
            }
            else if (favour < 70)
            {
                m.Write(At(5), 0, false);
                m.Write(At(6), 0, false);
                m.Write(At(7), 0xFF, false);
                m.Write(At(8), 0, false);
            }
        }
    }

    /// <summary>
    /// 나라 사이 형편(<c>0x0042BEA0</c>) — 여섯 나라 짝마다 맞섬 바이트(나라 +0x12 + 상대)의 아래 네 비트를 표 <c>0x00458DF0</c> 로 고친다
    /// (<c>0x0042BEF0</c>). 표 자리는 사이(+0x0B + 상대) 갈래 (사이 + 9) / 40, 세기 갈래(제 세기 / 2 &gt; 상대 0, 제 &lt; 상대 / 2 이면 1,
    /// 제 ≤ 상대 3, 아니면 2), 깃발(0x20 이면 0, 0x10 이면 1, 없으면 2). 값 7 은 rand(100) &lt; 사이면 3, 8 은 사이 &lt; rand(100) 이면 2,
    /// 9 · 10 은 사이 &lt; rand(20) + 30 · rand(10) + 30 이면 2, 아니면 0. 그 밖의 값은 그대로.
    /// 그러고 나서(<c>0x0042C020</c>) 바이트 전체가 1 이면 사이가 두 쪽 다 rand(5) + 3 오르고, 4 면 싸움 —
    /// rand(50) &lt; min(50, 제 세기 x 10 / 상대 세기)이면 상대의 (본거지 아닌) 마지막 항구를 빼앗는다(<c>0x0042C0D0</c>: 임자를 바꾸고
    /// 몫 rand(10) + 80, 나라 +0x19 + 상대에 그 항구). 4 는 깃발 0x10 칸에서만 나와 바이트 전체가 4 가 될 수 없다 — 원본 그대로 옮긴다.
    /// </summary>
    public static void Relations(ScenarioMemory m, Func<int, int> rand, Func<int, int> portOwner, Action<int, int> conquer, int portCount = 100)
    {
        ReadOnlySpan<byte> table =
        [
            0, 3, 0, 1, 3, 5, 0, 3, 1, 1, 3, 5,
            6, 4, 9, 1, 3, 10, 6, 7, 9, 0, 3, 10,
            6, 4, 2, 6, 3, 1, 6, 7, 8, 6, 7, 8,
        ];
        ScenarioMemory.Pointer At(int n, int f) => ScenarioMemory.At(ScenarioMemory.NationTable, n, f);
        int Byte(int n, int f) => m.Read(At(n, f), false);
        int Power(int n) => (short)m.Read(At(n, PowerField), true);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
            {
                if (i == j) { m.Write(At(i, 0x12 + j), 0, false); continue; }
                int pi = Power(i), pj = Power(j);
                int band = pi / 2 > pj ? 0 : pi < pj / 2 ? 1 : pi <= pj ? 3 : 2;
                int flags = Byte(i, 0x12 + j);
                int col = (flags & 0x20) != 0 ? 0 : (flags & 0x10) != 0 ? 1 : 2;
                int rel = Byte(i, RelationField + j);
                int v = table[3 * band + 12 * ((rel + 9) / 40) + col];
                int state = v switch
                {
                    7 => rand(100) < rel ? 3 : 0,
                    8 => rel < rand(100) ? 2 : 0,
                    9 => rel < rand(20) + 30 ? 2 : 0,
                    10 => rel < rand(10) + 30 ? 2 : 0,
                    _ => v,
                };
                m.Write(At(i, 0x12 + j), (flags & 0xF0) | (state & 0x0F), false);

                int now = Byte(i, 0x12 + j);
                if (now == 1)
                {
                    int up = (Byte(j, RelationField + i) + rand(5) + 3) & 0xFF;
                    m.Write(At(j, RelationField + i), up, false);
                    m.Write(At(i, RelationField + j), up, false);
                }
                else if (now == 4)
                {
                    int chance = pj != 0 && pi * 10 / pj < 50 ? pi * 10 / pj : 50;
                    if (rand(50) >= chance) continue;
                    for (int p = portCount - 1; p >= 0; p--)
                        if ((portOwner(p) & 7) == j && Byte(j, 0x0A) != p)
                        {
                            conquer(p, i);
                            m.Write(At(i, 0x19 + j), p, false);
                            break;
                        }
                }
            }
    }

    /// <summary>
    /// 왕궁 세력상황의 바다 갈래 한 줄(<c>0x004271A0</c>) — 그 갈래의 항로가 이어졌으면 수익(경제 합 / 나눗수), 끊겼으면 null.
    /// 갈래 0 · 1 은 그 갈래 공업 합이 300 이상, 갈래 2 는 (갈래 2 나 4) 와 갈래 3 이 300 이상이면 <b>갈래 3</b> 의 수익을,
    /// 갈래 3 ~ 7 은 갈래 4 부터 그 갈래까지가 모두 300 이상이어야 한다. 셈은 <see cref="Powers"/> 와 같다(비트 8 이면 경제 두 배).
    /// </summary>
    public static int? RouteIncome(int nation, int region, IReadOnlyList<(int Owner, bool Major, int Economy, int Industry, int Region)> ports)
    {
        if (nation < 0 || nation >= Count || region is < 0 or > 7) return null;
        long[] industry = new long[8], economy = new long[8];
        foreach (var p in ports)
        {
            if (p.Owner != nation) continue;
            industry[p.Region & 7] += p.Industry;
            economy[p.Region & 7] += p.Economy * (p.Major ? 2 : 1);
        }
        const int Floor = 300;
        switch (region)
        {
            case 0 or 1:
                return industry[region] >= Floor ? (int)(economy[region] / RegionDivisor[region]) : null;
            case 2:
                return (industry[2] >= Floor || industry[4] >= Floor) && industry[3] >= Floor
                    ? (int)(economy[3] / RegionDivisor[3]) : null;
            default:
                for (int r = 4; r <= region; r++)
                    if (industry[r] < Floor) return null;
                return (int)(economy[region] / RegionDivisor[region]);
        }
    }

    /// <summary>
    /// 달마다 — 세기를 적고 노리는 나라 · 방침을 고친다. <paramref name="favour"/> 는 나라마다 주인공 호감(주인공 표 +6 + 나라).
    /// </summary>
    public static void NewMonth(ScenarioMemory m, IReadOnlyList<(int Owner, bool Major, int Economy, int Industry, int Region)> ports,
                                Func<int, int> favour, int heroNation, Func<int, int> rand, Func<int, int>? portOwner = null)
    {
        ScenarioMemory.Pointer At(int n, int f) => ScenarioMemory.At(ScenarioMemory.NationTable, n, f);
        int Read(int n, int f) => f == PowerField ? m.Read(At(n, f), true) : m.Read(At(n, f), false);
        var power = Powers(ports);
        for (int n = 0; n < Count; n++) m.Write(At(n, PowerField), power[n] & 0xFFFF, true);
        for (int n = 0; n < Count; n++) m.Write(At(n, TargetField), Target(n, Read, favour(n), rand), false);
        for (int n = 0; n < Count; n++) m.Write(At(n, PolicyField), Policy(n, Read, heroNation, rand), false);
        if (portOwner != null)
            for (int n = 0; n < 6; n++) m.Write(At(n, PortField), MerchantPort(n, Read, portOwner, heroNation, rand, ports.Count), false);
    }

    /// <summary>
    /// 하루 끝의 항구 편(<c>0x00417CD0</c>) — 항구마다 나라 몫(형편 +8 ~ +0x0D)을 보고: 몫이 75 이상인 나라가 임자가 아니면 임자로 삼고
    /// (<c>0x00417DB0</c>, 경제(+0) 150 아래이고 항구 번호가 41 넘으면 쇠락 비트 8 을 세운다), 75 아래인 나라가 임자면 임자 없음(6)으로 둔다
    /// (<c>0x00417DE0</c>, 비트 8 도 지운다). 쇠락 비트가 선 항구가 경제 150 이상이 되면 비트를 지우고 값 지수 열(+0x0E)을 저마다
    /// min(rand(5) + 10, 100 − 지수)만큼 올린다(<c>0x00417E00</c>). 끝으로 +0x23(짓는 배)이 0xFF 가 아니면 0 까지 하나 줄인다.
    /// 임자는 항구 표 +0x15 의 아래 세 비트(<paramref name="getFlags"/> · <paramref name="setFlags"/>)다.
    /// </summary>
    public static void PortsDay(byte[] states, Func<int, int> getFlags, Action<int, int> setFlags, Func<int, int> rand)
    {
        const int Size = Support.Local.Formats.Economy.StateSize;
        for (int p = 0; p < 100 && (p + 1) * Size <= states.Length; p++)
        {
            int o = p * Size;
            int flags = getFlags(p);
            for (int k = 0; k < 6; k++)
            {
                int owner = flags & 7;
                if (states[o + 8 + k] >= 75)
                {
                    if (owner == k) continue;
                    flags = (flags & 0xF8) | k;
                    if (BitConverter.ToUInt16(states, o) < 150 && p > 0x29) flags |= 8;
                }
                else if (owner == k) flags = (flags & 0xF6) | 6;
            }
            if ((flags & 8) != 0 && BitConverter.ToUInt16(states, o) >= 150)
            {
                flags &= 0xF7;
                for (int i = 0; i < 10; i++)
                    states[o + 0x0E + i] += (byte)Math.Min(rand(5) + 10, 100 - states[o + 0x0E + i]);
            }
            if (states[o + 0x23] is not 0xFF and > 0) states[o + 0x23]--;
            setFlags(p, flags);
        }
    }

    /// <summary>해전 뒤 나라 셈이 낸 것 — 0 없음 · 1 국적 박탈(0x238) · 2 작위 박탈(0x239).</summary>
    public const int Kept = 0, LostNation = 1, LostTitle = 2;

    /// <summary>
    /// 해전 뒤 나라 셈(<c>0x0040BA40</c>, 끝 값이 짝수일 때) — 두 나라 사이(나라 표 +0x0B + 상대, 양쪽 같게)를 min(rand(5) + 3, 사이)만큼 내리고,
    /// 사략 덤 갈래(<c>0x0040B0E0</c>)로 (덤 d, 무게 w) = 2 → (2, 10) · 3 → (3, 6) · 4 → (4, 3) · 5 → (−1, 90) · 6 → (−1, 60) · 그 밖 (1, 30).
    /// 적 나라가 해적(6)이 아니면 그 나라 호의(주인공 표 +6 + 나라)를 min((rand(5) + 10) x w / 30, 호의)만큼 내린다. 주인공에 나라가 없으면 끝.
    /// 제 나라 함대를 쳤으면 제 나라 호의를 min(rand(5) + 10, 호의) 내리고, 40 이하면 국적(사람 +0x31 아래 네 비트 = 6)과 작위를,
    /// 작위가 있고 70 이하면 작위를 잃는다. 그 밖은 d ≥ 0 이면 제 나라 호의 += (rand(5) + 5) x d(200 까지), 아니면 −min(rand(5) + 5, 호의).
    /// </summary>
    public static int AfterBattle(ScenarioMemory m, int hero, int heroNation, int enemyNation, int bounty, Func<int, int> rand)
    {
        var rel = ScenarioMemory.At(ScenarioMemory.NationTable, heroNation, RelationField + enemyNation);
        var back = ScenarioMemory.At(ScenarioMemory.NationTable, enemyNation, RelationField + heroNation);
        int r = m.Read(rel, false);
        r -= Math.Min(rand(5) + 3, r);
        m.Write(rel, r, false);
        m.Write(back, r, false);
        ScenarioMemory.Pointer Favour(int n) => ScenarioMemory.At(ScenarioMemory.HeroTable, hero, 6 + n);
        var (d, w) = bounty switch { 2 => (2, 10), 3 => (3, 6), 4 => (4, 3), 5 => (-1, 90), 6 => (-1, 60), _ => (1, 30) };
        if (enemyNation != Pirates)
        {
            int f = m.Read(Favour(enemyNation), false);
            m.Write(Favour(enemyNation), f - Math.Min((rand(5) + 10) * w / 30, f), false);
        }
        if (heroNation == Pirates) return Kept;
        int own = m.Read(Favour(heroNation), false);
        if (heroNation == enemyNation)
        {
            own -= Math.Min(rand(5) + 10, own);
            m.Write(Favour(heroNation), own, false);
            var rank = ScenarioMemory.At(ScenarioMemory.HeroTable, hero, ScenarioMemory.RankField);
            if (own <= 40)
            {
                m.Write(rank, 0, false);
                var kind = ScenarioMemory.At(ScenarioMemory.PeopleTable, hero, 0x31);
                m.Write(kind, (m.Read(kind, false) & 0xF0) | Pirates, false);
                return LostNation;
            }
            if (m.Read(rank, false) != 0 && own <= 70)
            {
                m.Write(rank, 0, false);
                return LostTitle;
            }
            return Kept;
        }
        if (d >= 0) m.Write(Favour(heroNation), Math.Min(200, own + (rand(5) + 5) * d), false);
        else m.Write(Favour(heroNation), own - Math.Min(rand(5) + 5, own), false);
        return Kept;
    }
}
