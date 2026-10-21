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
/// 원조 쌓기(<c>0x0042B540</c>)도 옮겼다. 맞섬(<c>0x0042BEA0</c>) · 함대 다시 짓기(<c>0x0042ACF0</c> ~ <c>0x0042B3E0</c>)는 아직이다.
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
}
