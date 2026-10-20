namespace Uw2.Game.Engine.Town;

/// <summary>
/// 주점 「선원 고용」의 셈 — <c>KOUKAI2.EXE 0x004241B0</c>.
/// </summary>
/// <remarks>
/// <code>
///   빈 자리   배마다 최대 선원(배 기록 +0x12) − 지금 선원. 하나도 없으면 「더 태울 자리가 없다」(0x00424330)
///   돈        10 아래면 못 쓴다
///   매력 몫 S  주인공 능력치 +0x22(일곱째) / 3 (0x00426CAD). 30 아래면 원본은 한 번 더 묻는다
///   올 사람    min(항구 경제 x S / 500, (작위 + 1) x S) — 작위는 0 없음 · 1 사작 … 9 공작(0x00424390)
///   한 사람 값 항구 경제 / 20 + 5 (0x00424461)
///   쓸 수 있는 수  min(올 사람, 돈 / 값, 빈 자리 합)
/// </code>
/// 원본은 고용한 뒤 배마다 몇 명을 태울지 고르게 하고(0x00424540), 새로 탄 사람만큼 사기를 60 쪽으로 끌어당긴다
/// (<c>60 + (사기 − 60) x 전 선원 / 새 선원</c>). 여기서는 앞 배부터 빈 자리를 채우고 사기는 아직 셈하지 않는다.
/// </remarks>
public static class Pub
{
    public const int MinGold = 10;

    public static int Price(int economy) => economy / 20 + 5;

    public static int Available(int economy, int charm, int rank)
    {
        int s = charm / 3;
        return Math.Max(0, Math.Min(economy * s / 500, (rank + 1) * s));
    }

    /// <summary>쓸 수 있는 수 — 올 사람 · 돈 · 빈 자리 가운데 작은 것.</summary>
    public static int MostHirable(int available, int gold, int price, int vacancy) =>
        Math.Max(0, Math.Min(available, Math.Min(price > 0 ? gold / price : 0, vacancy)));
}
