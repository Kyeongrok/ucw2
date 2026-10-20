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

        /// <summary>선원 — 함대 칸 +0(낱말). 새로 지은 배는 0 이다(<c>0x0043DF6A</c>).</summary>
        public int Crew { get; set; }

        /// <summary>최대 선원 — 배 기록 +0x12(선원 자리).</summary>
        public int MaxCrew { get; init; }

        /// <summary>내구 · 최대 내구 — 함대 칸 +2 · +3.</summary>
        public int Hull { get; set; }
        public int MaxHull { get; set; }

        /// <summary>지금 역풍 · 돛 성능 — 함대 칸 +4 · +5. 수리하면 선종 값으로 돌아온다.</summary>
        public int Upwind { get; set; }
        public int Sail { get; set; }

        /// <summary>등급 — 배 기록 +0x10 의 아래 세 비트. 배 값에 든다(<see cref="Town.Shipyard.Value"/>).</summary>
        public int Grade { get; init; }

        public int Vacancy => Math.Max(0, MaxCrew - Crew);
    }

    public int Gold { get; private set; }
    public List<Ship> Ships { get; } = [];

    public PlayerState(int gold) => Gold = Math.Clamp(gold, 0, MaxGold);

    /// <summary>은행 잔고 — 0 보다 크면 예금, 작으면 빚(<see cref="Town.Bank"/>).</summary>
    public int Balance { get; private set; }

    /// <summary>돈과 잔고 사이로 옮긴다 — 양수는 돈 → 잔고(예금 · 상환), 음수는 잔고 → 돈(출금 · 대출).</summary>
    public void Transfer(int amount)
    {
        Gold = Math.Clamp(Gold - amount, 0, MaxGold);
        Balance += amount;
    }

    /// <summary>달이 바뀔 때 — 이자를 붙인다.</summary>
    public void NewMonth() => Balance = Town.Bank.WithInterest(Balance);

    /// <summary>돈을 바로 매긴다 — 시나리오 돈 명령(E4 · E6 · E7)이 쓴다.</summary>
    public void SetGold(int gold) => Gold = Math.Clamp(gold, 0, MaxGold);

    /// <summary>모든 배의 빈 자리.</summary>
    public int Free => Ships.Sum(s => s.Free);

    public const int Water = 0, Food = 1;

    public const int FullRation = 100;

    /// <summary>물 · 식량 비율 0~100(<c>0x0045BDA9</c> · <c>0x0045BDAA</c>) — 새 게임은 100(SDATA 0x48DA · 0x48DB), 「식량할당」 따위가 올리고 내린다(<c>0x00441800</c>).</summary>
    public int WaterRate { get; set; } = FullRation;
    public int FoodRate { get; set; } = FullRation;

    /// <summary>
    /// 바다의 하루 — 배마다 식량 다음 물을 먹는다(<c>0x00417480</c> · <c>0x004176F0</c>).
    /// 먹는 양은 <c>선원 x 비율 / 100 + 1</c>(<see cref="DailyUse"/>)이고 제 배에 없으면 그것이 있는 앞 배에서 꺼낸다.
    /// 아무 배에도 없던 것의 수를 돌려준다(원본은 그때 사기를 깎는다 — 0x004176B0).
    /// </summary>
    public int ConsumeDay()
    {
        int lacking = 0;
        foreach (var ship in Ships)
            foreach (int kind in (int[])[Food, Water])
            {
                int need = DailyUse(ship.Crew, kind == Food ? FoodRate : WaterRate);
                var from = ship.Supplies[kind] > 0 ? ship : Ships.FirstOrDefault(s => s.Supplies[kind] > 0);
                if (from == null) { lacking++; continue; }
                from.Supplies[kind] -= Math.Min(need, from.Supplies[kind]);
            }
        return lacking;
    }

    /// <summary>하루에 먹는 양 — <c>선원 x 비율 / 100 + 1</c>(<c>0x004175C3</c>). 원본은 깃발 0x0045A1A8 &amp; 0x1800 이면 / 200 이다.</summary>
    public static int DailyUse(int crew, int rate) => crew * rate / 100 + 1;

    /// <summary>모든 배의 선원 빈 자리.</summary>
    public int Vacancy => Ships.Sum(s => s.Vacancy);

    /// <summary>선원을 고용한다 — 앞 배부터 빈 자리를 채우고 한 사람 값을 낸다. 실제로 쓴 수를 돌려준다.</summary>
    public int Hire(int count, int price)
    {
        if (price > 0) count = Math.Min(count, Gold / price);
        int hired = 0;
        foreach (var ship in Ships)
        {
            int n = Math.Min(count - hired, ship.Vacancy);
            if (n <= 0) continue;
            ship.Crew += n;
            hired += n;
        }
        Gold -= hired * price;
        return hired;
    }

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
