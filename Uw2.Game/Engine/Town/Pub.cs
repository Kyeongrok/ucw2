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

    public static int Available(int economy, int charm, int rank) => AvailableAt(economy, Mood(charm), rank);

    /// <summary>주점 분위기 S(<c>0x0049A1B0</c>) — 들어올 때 매력 / 3(<c>0x00426CBF</c>), 한턱내면 오른다.</summary>
    public static int Mood(int charm) => charm / 3;

    public static int AvailableAt(int economy, int mood, int rank) => Math.Max(0, Math.Min(economy * mood / 500, (rank + 1) * mood));

    /// <summary>분위기가 이보다 낮으면 고용 앞에 술을 더 내라고 한다(0x1F · 0x20).</summary>
    public const int LowMood = 30;

    /// <summary>
    /// 「한턱내기」(<c>0x00424880</c>) — 술 n 병을 내면 분위기 += 매력 x (n x 1000 / 경제) / 10, 100 까지.
    /// 한 병 값은 항구 형편 +0x24 갈래의 술값, 병은 50 까지(가진 돈 mod 10000 에 1만 넘으면 1만을 더한 것 / 값).
    /// </summary>
    public static int Treat(int mood, int bottles, int economy, int charm) =>
        Math.Min(100, mood + charm * (bottles * 1000 / Math.Max(1, economy)) / 10);

    public static int MostBottles(int gold, int price) =>
        price <= 0 ? 0 : Math.Min(50, (gold % 10000 + (gold >= 10000 ? 10000 : 0)) / price);

    /// <summary>술 이름 — 갈래마다(<c>0x00457148</c>, 11바이트씩).</summary>
    public static readonly string[] Drinks = ["럼주", "와인", "위스키", "브랜디", "진", "맥주", "보드카", "테킬라", "야자주", "유주"];

    /// <summary>쓸 수 있는 수 — 올 사람 · 돈 · 빈 자리 가운데 작은 것.</summary>
    public static int MostHirable(int available, int gold, int price, int vacancy) =>
        Math.Max(0, Math.Min(available, Math.Min(price > 0 ? gold / price : 0, vacancy)));
}
