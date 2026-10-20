namespace Uw2.Game.Engine.Town;

/// <summary>
/// 주점 「도박」 블랙잭(<c>0x00406DA0</c>) — 카드 쉰둘(0 ~ 51, 무늬 = c / 13, 숫자 = c % 13 + 1)을 섞어(<c>0x00407280</c>)
/// 주인공 0 · 2, 딜러 1 · 3 을 돌리고 그 뒤를 차례로 뽑는다.
/// </summary>
/// <remarks>
/// <code>
///   카드 값      숫자, 10 넘으면 10(<c>0x004075E0</c>). 에이스가 있고 합 ≤ 11 이면 +10(<c>0x004073E0</c>). 21 을 넘으면 0
///   걸기         10 ~ min(소지금, 500) — 건 돈은 먼저 낸다
///   딜러         17 아래면 뽑는다(넷까지). 딜러 합 ≥ 주인공 합이면 딜러가 이긴다(비기면 딜러)
///   주인공이 이김(<c>0x00407653</c>) — 돌려받는 몫 x 건 돈
///     21 두 장   ♠A + ♠J 6(0x3C6, 5배) · 그림 카드와 에이스 3(0x3C7) · 10 과 에이스 2(0x3C8)
///     21 석 장   같은 숫자 셋(7 7 7) 4(0x3C9) · 이어진 숫자(6 7 8) 4(0x3CA) · 그 밖 2
///     그 밖      다섯 장 넘게 4(0x3CB) · 아니면 2(0x3C8)
///   딜러가 이김(<c>0x004077D0</c>) — 더 내는 몫 x 건 돈(건 돈은 이미 잃었다)
///     21 두 장   ♠A + ♠J 4(0x3CC) · 그림 카드와 에이스 1(0x3CD) · 10 과 에이스 0
///     21 석 장   같은 숫자 셋 · 이어진 숫자 2(0x3CF · 0x3D0)
///     다섯 장 넘게(터지지 않고) 2(0x3D1) · 그 밖 0(0x3CE)
///   더 낼 돈이 소지금보다 많으면 소지금이 0 이 된다
/// </code>
/// </remarks>
public static class BlackJack
{
    public const int MinBet = 10, MaxBet = 500, DealerStands = 17, MaxPlayerCards = 7, MaxDealerCards = 6;

    public static int Rank(int card) => card % 13 + 1;

    public static int Value(int card) => Math.Min(10, Rank(card));

    public static string Name(int card) =>
        "♠♥♦♣"[card / 13 % 4] + (Rank(card) switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", var r => r.ToString() });

    public static int Total(IReadOnlyList<int> cards)
    {
        int sum = cards.Sum(Value);
        if (cards.Any(c => Rank(c) == 1) && sum <= 11) sum += 10;
        return sum;
    }

    /// <summary>터지면 0.</summary>
    public static int Score(IReadOnlyList<int> cards) => Total(cards) is var t && t > 21 ? 0 : t;

    public static int[] Shuffle(Func<int, int> rand)
    {
        var deck = Enumerable.Range(0, 52).ToArray();
        for (int i = 0; i < deck.Length; i++)
        {
            int j = rand(52);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }

    public static int MostBet(int gold) => Math.Min(gold, MaxBet);

    private static bool SpadeJack(IReadOnlyList<int> h) => h.Count == 2 && (h[0], h[1]) is (0, 10) or (10, 0);

    private static bool AceAndTen(IReadOnlyList<int> h) => h.All(c => Rank(c) <= 10);

    private static int Triple(IReadOnlyList<int> h)
    {
        var r = h.Select(Rank).Order().ToArray();
        if (r[0] == r[1] && r[1] == r[2]) return 1;
        return r[1] == r[0] + 1 && r[2] == r[1] + 1 ? 2 : 0;
    }

    /// <summary>판을 가른다 — (주인공이 받는 몫, 딜러에게 더 내는 몫, 말). 소지금 변화는 (받는 몫 − 더 내는 몫) x 건 돈.</summary>
    public static (int Win, int Loss, int Message) Settle(IReadOnlyList<int> player, IReadOnlyList<int> dealer)
    {
        int p = Score(player), d = Score(dealer);
        if (d < p)
        {
            if (p == 21 && player.Count == 2)
                return SpadeJack(player) ? (6, 0, 0x3C6) : AceAndTen(player) ? (2, 0, 0x3C8) : (3, 0, 0x3C7);
            if (p == 21 && player.Count == 3)
                return Triple(player) switch { 1 => (4, 0, 0x3C9), 2 => (4, 0, 0x3CA), _ => (2, 0, 0x3C8) };
            return player.Count >= 5 ? (4, 0, 0x3CB) : (2, 0, 0x3C8);
        }
        if (d == 21 && dealer.Count == 2)
            return SpadeJack(dealer) ? (0, 4, 0x3CC) : AceAndTen(dealer) ? (0, 0, 0x3CE) : (0, 1, 0x3CD);
        if (d == 21 && dealer.Count == 3 && Triple(dealer) is var t and not 0)
            return (0, 2, t == 1 ? 0x3CF : 0x3D0);
        return dealer.Count >= 5 && d != 0 ? (0, 2, 0x3D1) : (0, 0, 0x3CE);
    }

    /// <summary>건 돈을 이미 낸 소지금에 판 결과를 더한다 — 더 낼 돈이 모자라면 0.</summary>
    public static int Pay(int gold, int bet, int win, int loss) =>
        loss > 0 && gold < loss * bet ? 0 : Math.Min(PlayerState.MaxGold, gold + (win - loss) * bet);
}
