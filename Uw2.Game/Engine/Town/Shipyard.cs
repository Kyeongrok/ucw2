using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Town;

/// <summary>
/// 조선소 「수리」 · 「매각」의 셈 — <c>KOUKAI2.EXE 0x00429C30 · 0x00429D60</c>.
/// </summary>
/// <remarks>
/// <code>
///   새 배      내구 = 최대 내구 = 선종 내구 x 9 / 10, 역풍 · 돛은 선종 그대로(0x0043DF3A)
///   짓기       등급을 고른다 — 3 + (공업 − 500) / 200 가지. 값 = 등급 x 값 / 10 + 4 x 값 / 5 (0x004291C0)
///   수리 값    20 x ((선종 역풍 − 역풍) + (선종 돛 − 돛) + (최대 내구 − 내구)) — 고치면 내구가 최대 내구로,
///              역풍 · 돛이 선종 값으로 돌아온다. 최대 내구는 그대로다. 0 이면 「고칠 곳이 없다」
///   배 값      ((등급 x 값 / 10 + 4 x 값 / 5) x (항구 값 지수 열의 평균 + 50) / 100) 을 10 에서 자른 것(0x00429040)
///              등급은 배 기록 +0x10 의 아래 세 비트, 값 지수는 항구 형편 +0x0E 부터 열(0x00431AB0)
///   매각 값    배 값 / 2. 마지막 한 척은 못 판다. 짐이나 선원이 있으면 원본은 한 번 더 묻는다
/// </code>
/// </remarks>
public static class Shipyard
{
    public static int NewHull(StartData.ShipType type) => type.Durability * 9 / 10;

    /// <summary>
    /// 지을 때 고르는 등급 수(<c>0x00429A00</c>) — 3 + (공업 − 500) / 200 (공업 500 이하면 3). 선종 0x18 은 늘 6.
    /// </summary>
    public static int GradeChoices(int type, int industry) => type == 0x18 ? 6 : 3 + Math.Max(0, industry - 500) / 200;

    /// <summary>짓는 값 — 등급 x 값 / 10 + 4 x 값 / 5 (<c>0x004292C2</c>). 배 값에서 항구 지수를 뺀 꼴이다.</summary>
    public static int BuildPrice(int price, int grade) => grade * price / 10 + 4 * price / 5;

    public static int RepairCost(StartData.ShipType type, PlayerState.Ship ship) =>
        20 * Math.Max(0, (type.Upwind - ship.Upwind) + (type.Sail - ship.Sail) + (ship.MaxHull - ship.Hull));

    public static void Repair(StartData.ShipType type, PlayerState.Ship ship)
    {
        ship.Hull = ship.MaxHull;
        ship.Upwind = type.Upwind;
        ship.Sail = type.Sail;
    }

    public static int PriceIndexAverage(IReadOnlyList<int> priceIndex) =>
        priceIndex.Count == 0 ? 50 : priceIndex.Take(10).Sum() / 10;

    public static int Value(int price, int grade, int priceIndexAverage) =>
        (grade * price / 10 + 4 * price / 5) * (priceIndexAverage + 50) / 100 / 10 * 10;

    public static int SellPrice(int price, int grade, int priceIndexAverage) => Value(price, grade, priceIndexAverage) / 2;
}
