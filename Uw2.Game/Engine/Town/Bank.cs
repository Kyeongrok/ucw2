namespace Uw2.Game.Engine.Town;

/// <summary>
/// 은행 「예금 · 출금 · 대출 · 상환」 — <c>KOUKAI2.EXE 0x0041E410 · 0x0041E540 · 0x0041E640 · 0x0041E780</c>.
/// </summary>
/// <remarks>
/// 통장은 하나다 — 부호 있는 잔고(<c>0x0049F5AC</c> 낱말 x 100 + <c>0x0049F5AE</c> 바이트). 0 보다 크면 예금, 작으면 빚이다.
/// <code>
///   예금  돈이 1000 을 넘어야 하고 빚이 없어야 한다. 잔고는 1,000,000 까지 — 넣을 수 있는 것 min(돈, 1,000,000 − 잔고)
///   출금  예금이 있어야 한다. 돈이 6억을 넘지 않게
///   대출  예금이 없어야 한다. 빌릴 수 있는 것 1000 + 10000 x 작위² + 잔고(작위 0 없음 … 9 공작). 돈이 100만 넘으면 한마디 한다
///   상환  빚이 있어야 한다 — min(빚, 돈)
///   이자  달이 바뀔 때(0x0043AA52 → 0x0042B800) 예금 3% · 빚 10% 를 잔고에 더하고 ±1,000,000 에서 자른다
/// </code>
/// </remarks>
public static class Bank
{
    public const int DepositFloorGold = 1000, Cap = 1_000_000;

    public static int MaxDeposit(int gold, int balance) =>
        gold <= DepositFloorGold || balance < 0 || balance >= Cap ? 0 : Math.Min(gold, Cap - balance);

    public static int MaxWithdraw(int gold, int balance) =>
        balance <= 0 ? 0 : (int)Math.Min(balance, (long)PlayerState.MaxGold - gold);

    public static int MaxLoan(int balance, int rank) =>
        balance > 0 ? 0 : Math.Max(0, 1000 + 10000 * rank * rank + balance);

    public static int MaxRepay(int gold, int balance) => balance >= 0 ? 0 : Math.Min(-balance, gold);

    /// <summary>한 달 이자를 붙인 잔고.</summary>
    public static int WithInterest(int balance)
    {
        int rate = balance < 0 ? 10 : 3;
        long b = balance + (long)balance * rate / 100;
        return (int)Math.Clamp(b, -Cap, Cap);
    }
}
