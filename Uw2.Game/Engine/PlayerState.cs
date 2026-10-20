namespace Uw2.Game.Engine;

/// <summary>
/// 제 함대의 돈과 배마다의 짐 — 원본의 돈 <c>0x0049F5A8</c> 과 제 함대 칸 곁 30바이트(<c>0x004610B0</c>)를 옮긴 것.
/// </summary>
/// <remarks>
/// 칸 곁 30바이트에서 짐 자리는 이렇다(새 배 채비 <c>0x0043DF74</c> ~ <c>0x0043DFB3</c>).
/// <code>
///   +0x00 ~ +0x07   보급 넷(낱말) — 물 · 식량 · 자재 · 탄약
///   +0x0C ~ +0x15   교역품 다섯 칸의 양(낱말)
///   +0x16 ~ +0x1A   교역품 다섯 칸의 번호(0xFF 빈 칸)
/// </code>
/// 실을 수 있는 양은 배 기록의 짐 자리(+0x14 = 짐칸 - 대포 - 선원 자리)다. 돈은 6억에서 자른다(<c>0x23C34600</c>).
/// </remarks>
public sealed class PlayerState
{
    public const int MaxGold = 600_000_000, CargoSlots = 5, SupplyKinds = 4, Lot = 10;

    public sealed class Ship
    {
        public required int Type { get; init; }
        public required string Name { get; init; }

        /// <summary>짐 자리 — 배 기록 +0x14.</summary>
        public required int Room { get; init; }

        /// <summary>보급 넷(통) — 물 · 식량 · 자재 · 탄약.</summary>
        public int[] Supplies { get; } = new int[SupplyKinds];

        /// <summary>교역품 다섯 칸 — (번호, 양). 번호 -1 이 빈 칸.</summary>
        public (int Good, int Amount)[] Cargo { get; } = Enumerable.Repeat((-1, 0), CargoSlots).ToArray();

        public int Load => Supplies.Sum() + Cargo.Sum(c => c.Amount);
        public int Free => Math.Max(0, Room - Load);
    }

    public int Gold { get; private set; }
    public List<Ship> Ships { get; } = [];

    public PlayerState(int gold) => Gold = Math.Clamp(gold, 0, MaxGold);

    /// <summary>모든 배의 빈 자리.</summary>
    public int Free => Ships.Sum(s => s.Free);

    /// <summary>
    /// 교역품을 산다 — 같은 교역품이 든 칸에 먼저, 없으면 빈 칸에 넣으며 배를 차례로 채운다. 실제로 산 양을 돌려준다.
    /// </summary>
    public int Buy(int good, int amount, int price)
    {
        if (price <= 0) return 0;
        amount = Math.Min(amount, Gold / price);
        int bought = 0;
        foreach (var ship in Ships)
        {
            while (amount - bought > 0 && ship.Free > 0)
            {
                int slot = Array.FindIndex(ship.Cargo, c => c.Good == good);
                if (slot < 0) slot = Array.FindIndex(ship.Cargo, c => c.Good < 0);
                if (slot < 0) break;
                int n = Math.Min(amount - bought, ship.Free);
                ship.Cargo[slot] = (good, ship.Cargo[slot].Amount + n);
                bought += n;
            }
        }
        Gold -= bought * price;
        return bought;
    }

    /// <summary>그 교역품을 다 판다. 판 양을 돌려준다.</summary>
    public int SellAll(int good, int price)
    {
        int sold = 0;
        foreach (var ship in Ships)
            for (int i = 0; i < CargoSlots; i++)
                if (ship.Cargo[i].Good == good)
                {
                    sold += ship.Cargo[i].Amount;
                    ship.Cargo[i] = (-1, 0);
                }
        Gold = (int)Math.Min(MaxGold, (long)Gold + (long)sold * price);
        return sold;
    }

    /// <summary>실은 교역품(번호 → 모두 합친 양).</summary>
    public Dictionary<int, int> CargoTotals() =>
        Ships.SelectMany(s => s.Cargo).Where(c => c.Good >= 0)
             .GroupBy(c => c.Good).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

    /// <summary>보급을 단(열 통)으로 산다 — 앞 배부터 채운다. 실제로 산 단을 돌려준다.</summary>
    public int BuySupply(int kind, int lots, int lotPrice)
    {
        int bought = 0;
        foreach (var ship in Ships)
            while (bought < lots && ship.Free >= Lot && (lotPrice == 0 || Gold >= lotPrice))
            {
                ship.Supplies[kind] += Lot;
                Gold -= lotPrice;
                bought++;
            }
        return bought;
    }
}
