using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Town;

/// <summary>
/// 조선소 「수리」 · 「매각」의 셈 — <c>KOUKAI2.EXE 0x00429C30 · 0x00429D60</c>.
/// </summary>
/// <remarks>
/// <code>
///   새 배      내구 = 최대 내구 = 선종 내구 x 9 / 10, 역풍 · 돛은 선종 그대로(0x0043DF3A)
///   중고선     항구에 들 때 마련한다(EnterPort). 값은 선종 값 그대로를 흥정 손(0x004295A0)에 넘긴다 — 흥정은 아직
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

    /// <summary>중고선 칸 — 시나리오 기억 표 15(<c>0x00460608</c>), 8바이트 x 3: 선종 다섯(0xFF 빈 칸) · 항구 · 둘.</summary>
    public const int UsedTable = 15, UsedRecords = 3, UsedSlots = 5, UsedPortField = 5;

    /// <summary>
    /// 값싼 것부터 늘어놓은 선종(<c>0x004581F0</c>) — 경제가 높을수록 뒤까지 뽑힌다. 원본은 열여덟 칸 뒤의 바이트까지 읽으므로
    /// 그 자리 그대로 마흔을 옮겨 둔다(0 은 발사, 0xFE · 0xFF 는 빈 칸으로 친다).
    /// </summary>
    public static ReadOnlySpan<byte> CheapFirst =>
        [0, 1, 18, 4, 5, 6, 2, 3, 19, 7, 8, 20, 12, 9, 21, 11, 10, 13, 0, 0, 0, 0, 0, 0,
         255, 254, 0, 254, 1, 254, 254, 255, 254, 0, 254, 1, 2, 255, 2, 0];

    /// <summary>
    /// 항구에 들 때 중고선을 마련한다(<c>0x0041A655</c>). 그 항구가 칸 셋 가운데 있으면 앞으로 옮기기만 하고,
    /// 없으면 칸을 하나씩 밀고 새로 뽑는다 — 앞 셋은 조선소 갈래 줄에서 공업이 되는 것의 수 n 만큼 <c>줄[주사위(n)]</c>,
    /// 뒤 둘은 <c>값싼 줄[주사위(min(경제, 680) / 20 + 1)]</c>.
    /// </summary>
    public static void EnterPort(ScenarioMemory m, int port, int yard, int industry, int economy,
                                 IReadOnlyList<StartData.ShipType> types, Func<int, int> rand)
    {
        byte[] Rec(int r) => Enumerable.Range(0, 8).Select(i => (byte)m.Read(ScenarioMemory.At(UsedTable, r, i), false)).ToArray();
        void Put(int r, byte[] b) { for (int i = 0; i < 8; i++) m.Write(ScenarioMemory.At(UsedTable, r, i), b[i], false); }
        var recs = Enumerable.Range(0, UsedRecords).Select(Rec).ToList();
        int hit = recs.FindIndex(r => r[UsedPortField] == port);
        if (hit >= 0)
        {
            var keep = recs[hit];
            recs.RemoveAt(hit);
            recs.Insert(0, keep);
        }
        else
        {
            var fresh = Enumerable.Repeat((byte)0xFF, 8).ToArray();
            fresh[UsedPortField] = (byte)port;
            var line = yard >= 0 && yard < Economy.ShipyardTypes.Length ? Economy.ShipyardTypes[yard] : [];
            int n = line.Count(t => t < types.Count && types[t].Field0 * 10 <= industry);
            int k = 0;
            if (n > 0)
                for (; k < 3; k++) fresh[k] = (byte)line[rand(n)];
            for (int j = 0; j < 2; j++, k++)
                fresh[k] = CheapFirst[rand(Math.Min(economy, 680) / 20 + 1)];
            recs.RemoveAt(UsedRecords - 1);
            recs.Insert(0, fresh);
        }
        for (int r = 0; r < UsedRecords; r++) Put(r, recs[r]);
    }

    /// <summary>지금 항구(칸 0)의 중고선 — (칸, 선종).</summary>
    public static List<(int Slot, int Type)> Used(ScenarioMemory m, int typeCount) =>
        Enumerable.Range(0, UsedSlots)
            .Select(i => (i, m.Read(ScenarioMemory.At(UsedTable, 0, i), false)))
            .Where(x => x.Item2 < typeCount).ToList();

    /// <summary>산 중고선 칸을 비운다(<c>0x00429C16</c>).</summary>
    public static void TakeUsed(ScenarioMemory m, int slot) => m.Write(ScenarioMemory.At(UsedTable, 0, slot), 0xFF, false);

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
