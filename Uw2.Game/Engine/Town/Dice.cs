namespace Uw2.Game.Engine.Town;

/// <summary>
/// 주점 「도박」 다이스(<c>0x004079D0</c>) — 셋(남 0 · 남 1 · 주인공)이 다섯 눈을 굴려 손을 겨룬다. 눈은 0 ~ 5(rand(6)).
/// docs/24 「주점 도박」.
/// </summary>
public static class Dice
{
    public const int Count = 5, MinBet = 10, MaxBet = 500, Rerolls = 2;

    /// <summary>남기지 않은 눈만 굴리고 작은 것부터 늘어놓는다(<c>0x00408A00</c> · <c>0x00408E10</c>).</summary>
    public static int[] Roll(int[] dice, bool[] keep, Func<int, int> rand)
    {
        var d = (int[])dice.Clone();
        for (int i = 0; i < Count; i++) if (!keep[i]) d[i] = rand(6);
        Array.Sort(d);
        return d;
    }

    /// <summary>남의 고르기(<c>0x00408D80</c>) — 다른 눈과 같은 눈은 다 남긴다.</summary>
    public static bool[] Keep(int[] d) =>
        [.. Enumerable.Range(0, Count).Select(i => Enumerable.Range(0, Count).Any(j => j != i && d[j] == d[i]))];

    /// <summary>손 크기(<c>0x004082E0</c>) — 같은 짝의 수. 다섯 10 · 넷 6 · 풀하우스 4 · 셋 3 · 두 짝 2 · 한 짝 1 · 없음 0.</summary>
    public static int Pairs(int[] d)
    {
        int n = 0;
        for (int i = 0; i < Count; i++)
            for (int j = i + 1; j < Count; j++)
                if (d[i] == d[j]) n++;
        return n;
    }

    /// <summary>첫 짝 눈(<c>0x00408840</c>) — 늘어놓은 눈에서 처음 같은 짝을 이루는 눈.</summary>
    private static int FirstPair(int[] d)
    {
        for (int i = 0; i < Count - 1; i++)
            for (int j = i + 1; j < Count; j++)
                if (d[i] == d[j]) return d[i];
        return -1;
    }

    /// <summary>짝 눈 가운데 <paramref name="not"/> 이 아닌 것(<c>0x00408880</c>).</summary>
    private static int OtherPair(int[] d, int not)
    {
        for (int i = 0; i < Count - 1; i++)
            for (int j = i + 1; j < Count; j++)
                if (d[i] == d[j] && d[i] != not) return d[i];
        return -1;
    }

    /// <summary>같은 손 크기끼리 가르는 열쇠 — 앞 것부터 견준다. 짝 없음은 null(원본이 가르지 않는다).</summary>
    private static int[]? Keys(int[] d, int pairs) => pairs switch
    {
        10 or 6 or 3 => [d[2]],
        4 => [d[2], OtherPair(d, d[2])],
        2 => [FirstPair(d), OtherPair(d, FirstPair(d))],
        1 => [FirstPair(d)],
        _ => null,
    };

    /// <summary>
    /// 가르기(<c>0x00408310</c>) — (주인공, 남 0, 남 1). 0 주인공 · 1 남 0 · 2 남 1 이 이김, 3 남 둘이 비김, 4 셋이 비김,
    /// 5 · 6 주인공이 남 0 · 1 과 비김. 가장 큰 손이 짝 없음끼리 같으면 원본은 가르지 않아 6 으로 친다(docs/24 — 원본 버그).
    /// </summary>
    public static int Judge(int[] hero, int[] a, int[] b)
    {
        int[][] hands = [hero, a, b];
        int[] size = [.. hands.Select(Pairs)];
        int top = size.Max();
        var best = Enumerable.Range(0, 3).Where(i => size[i] == top).ToList();
        if (best.Count > 1)
        {
            if (Keys(hero, top) == null) return 6;
            int[][] keys = [.. best.Select(i => Keys(hands[i], top)!)];
            for (int k = 0; k < keys[0].Length && best.Count > 1; k++)
            {
                int m = keys.Max(x => x[k]);
                var keep = best.Where((_, n) => keys[n][k] == m).ToList();
                keys = [.. keys.Where(x => x[k] == m)];
                best = keep;
            }
        }
        return best switch
        {
            [var w] => w,
            [1, 2] => 3,
            [0, 1, 2] => 4,
            [0, 1] => 5,
            _ => 6,
        };
    }

    /// <summary>
    /// 걸 돈 흥정(<c>0x00408E80</c>) — 남이 내놓는 값. b ≥ 200 이거나 rand(4) == 0 이면 그대로(null).
    /// 아니면 b + b x (rand(b ≥ 100 ? 5 : 10) + 1) / 10.
    /// </summary>
    public static int? Raise(int bet, Func<int, int> rand) =>
        bet >= 200 || rand(4) == 0 ? null : bet + bet * (rand(bet >= 100 ? 5 : 10) + 1) / 10;

    /// <summary>마다했을 때 — rand(3) == 0 이면 b 그대로(0x3DF 「아냐, %d닢으로 하지」), 아니면 rand(2) 면 p(0x3DC), 아니면 (b + p) / 2(0x3DD). (정한 값, 말).</summary>
    public static (int Bet, int Message) Refuse(int bet, int raise, Func<int, int> rand)
    {
        if (rand(3) == 0) return (bet, 0x3DF);
        return rand(2) != 0 ? (raise, 0x3DC) : ((bet + raise) / 2, 0x3DD);
    }

    /// <summary>판 끝 돈(<c>0x004080AA</c>) — 건 돈을 이미 낸 소지금에 더한다. 이기면 x 3, 셋이 비기면 x 1, 한 이와 비기면 x 3 / 2.</summary>
    public static int Payout(int result, int bet) => result switch
    {
        0 => bet * 3,
        4 => bet,
        5 or 6 => bet * 3 / 2,
        _ => 0,
    };
}
