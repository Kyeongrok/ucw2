using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 함대 다시 짓기(<c>KOUKAI2.EXE 0x0042B0F0</c>) — 달마다 바다에 없는 함대의 빈 칸에 배를 채우고(<c>0x0042ACF0</c> · <c>0x0042B110</c> ·
/// <c>0x0042B250</c>), 배가 넉넉해지면 제독을 세워 본거지에서 내보낸다(<c>0x0042B370</c> → <c>0x0042B3E0</c> → <c>0x0042B030</c>).
/// </summary>
/// <remarks>
/// 나라 n(해적 6 까지) 의 세기 p 로 갈래 = min(2, p / 500). 배 기록 번호는 표의 네 비트(rand(3) 번째) + 0x28(또는 0x30)이다.
/// <code>
///   상선 n1 ~ n4   p / 50 + 5 척까지   표 0x00458E18   기록 0x28 +   대포 갈래 rand(3) + 4
///   해군 n5 · n6   p / 75 + 5          표 0x00458E70   기록 0x30 +   대포 갈래 rand(3) + 1
///   해군 n7 ~ n9   빈 칸을 다           표 0x00458EC8   기록 0x28 +   대포 갈래 rand(3) + 1   (p / 75 + 4 함대까지 — 늘 셋 다)
/// </code>
/// 배 기록 40 ~ 64 는 선종 본보기(<see cref="StartData.ShipTemplateFirst"/>)라 기록 r 은 선종 r − 40 이다.
/// </remarks>
public static class FleetForge
{
    private static readonly int[] Merchant =
        [0x578, 0x789, 0x899, 0x778, 0x789, 0x999, 0x558, 0x889, 0x99B, 0x55C, 0x589, 0x889, 0x443, 0x433, 0x399, 0x158, 0x589, 0x999, 0xC88, 0x89A, 0x99A];
    private static readonly int[] NavyA =
        [0xABB, 0xBDC, 0xCCC, 0xABB, 0xBDC, 0xCCC, 0xAAB, 0xBBC, 0xCCC, 0x015, 0x122, 0x222, 0xADD, 0xDCC, 0xCCC, 0x011, 0x122, 0x222, 0xABB, 0xBCC, 0xCCC];
    private static readonly int[] NavyB =
        [0x899, 0x9AA, 0xAAA, 0x899, 0x9AA, 0xAAA, 0x899, 0x9BB, 0xBBB, 0x899, 0x9AA, 0xAAA, 0x833, 0x839, 0x9AA, 0x899, 0x9AA, 0xAAA, 0xCC8, 0x8AA, 0xAAA];

    /// <summary>나라 세기의 갈래(<c>0x0042AD28</c>).</summary>
    public static int Tier(int power) => Math.Min(2, Math.Max(0, power) / 500);

    /// <summary>
    /// 칸 하나를 배 기록으로 채운다(<c>0x0042AC30</c>) — 내구 = 선종 내구(지금 · 최대), 역풍 · 돛 = 선종, 대포 = 본보기 +0x11,
    /// 선원 = 본보기 선원 자리, 깃발 = (rand(3) + 4 − 3k) | 0x10 — k 는 0 상선 · 1 해군.
    /// </summary>
    public static StartData.Slot Slot(StartData start, int record, int k, Func<int, int> rand)
    {
        int type = Math.Clamp(record - StartData.ShipTemplateFirst, 0, start.ShipTypes.Count - 1);
        var st = start.ShipTypes[type];
        var t = type < start.Templates.Count ? start.Templates[type] : default;
        return new StartData.Slot(t.CrewRoom, st.Durability, st.Durability, st.Upwind, st.Sail, t.Cannons, record,
                                  (rand(3) + 4 - 3 * k) | 0x10);
    }

    /// <summary>
    /// 한 달 채우기 — 바다에 없는 함대(<paramref name="idle"/>)의 빈 칸을 나라마다 정해진 척수만큼 채운다. 바다에 있는 함대는 건너뛴다.
    /// </summary>
    public static void Fill(StartData start, Func<int, int> power, Func<int, bool> idle, Func<int, List<StartData.Slot>> reserve, Func<int, int> rand)
    {
        for (int n = 0; n < 7; n++)
        {
            int p = power(n), tier = Tier(p);
            Batch(n, 1, 5, p / 50 + 5, Merchant, 0x28, 0);
            Batch(n, 5, 7, p / 75 + 5, NavyA, 0x30, 1);
            Batch(n, 7, 10, int.MaxValue, NavyB, 0x28, 1);

            void Batch(int nation, int from, int to, int budget, int[] table, int baseRecord, int k)
            {
                for (int f = from; f < to && budget > 0; f++)
                {
                    int index = nation * 10 + f;
                    if (!idle(index)) continue;
                    var slots = reserve(index);
                    while (slots.Count < StartData.SlotCount && budget > 0)
                    {
                        int nib = table[nation * 3 + tier] >> (rand(3) * 4) & 0xF;
                        slots.Add(Slot(start, baseRecord + nib, k, rand));
                        budget--;
                    }
                }
            }
        }
    }

    /// <summary>내보낼 만한지(<c>0x0042B3B1</c>) — 배가 rand(2) + 4 척 넘게 찼다.</summary>
    public static bool Ready(int ships, Func<int, int> rand) => ships >= rand(2) + 4;
}
