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
    public const int MaxGold = 600_000_000, CargoSlots = 5, SupplyKinds = 4, Lot = 10, DefaultMorale = 60, MutinyBelow = 20;

    public sealed class Ship
    {
        public required int Type { get; init; }
        public required string Name { get; set; }

        /// <summary>짐 자리 — 배 기록 +0x14.</summary>
        public required int Room { get; set; }

        /// <summary>보급 넷(통) — 물 · 식량 · 자재 · 탄약.</summary>
        public int[] Supplies { get; } = new int[SupplyKinds];

        /// <summary>교역품 다섯 칸 — (번호, 양). 번호 -1 이 빈 칸.</summary>
        public (int Good, int Amount)[] Cargo { get; } = Enumerable.Repeat((-1, 0), CargoSlots).ToArray();

        public int Load => Supplies.Sum() + Cargo.Sum(c => c.Amount);
        public int Free => Math.Max(0, Room - Load);

        /// <summary>선원 — 함대 칸 +0(낱말). 새로 지은 배는 0 이다(<c>0x0043DF6A</c>).</summary>
        public int Crew { get; set; }

        /// <summary>최대 선원 — 배 기록 +0x12(선원 자리).</summary>
        public int MaxCrew { get; set; }

        /// <summary>실은 대포(배 기록 +0x11). 짐칸 = 선종 짐칸 − 선원 자리 − 대포다.</summary>
        public int Guns { get; set; }

        /// <summary>내구 · 최대 내구 — 함대 칸 +2 · +3.</summary>
        public int Hull { get; set; }
        public int MaxHull { get; set; }

        /// <summary>지금 역풍 · 돛 성능 — 함대 칸 +4 · +5. 수리하면 선종 값으로 돌아온다.</summary>
        public int Upwind { get; set; }
        public int Sail { get; set; }

        /// <summary>등급 — 배 기록 +0x10 의 아래 세 비트. 배 값에 든다(<see cref="Town.Shipyard.Value"/>).</summary>
        public int Grade { get; init; }

        /// <summary>사기 0 ~ 100 — 함대 칸 곁 +0x1C. 새로 맡는 배는 60(0x0040B927).</summary>
        public int Morale { get; set; } = DefaultMorale;

        /// <summary>단 대포 갈래 1 ~ 7 과 문 수 — 함대 칸 +8 의 아래 세 비트 · +6(<see cref="Town.Shipyard.GunNames"/>). 문 수는 <see cref="Guns"/> 까지.</summary>
        public int GunKind { get; set; }
        public int Mounted { get; set; }

        /// <summary>
        /// 승무원비율 — 칸 곁 +9 운항 · +8 파수 몫(%), 나머지(100 − 둘)가 갑판이다(말 0x379 「운항/파수/갑판」). 새 배는 50 · 25(<c>0x0043DF83</c>).
        /// 돛 몫은 빠르기 셈에, +8 몫은 보이는 거리에 든다(<see cref="Sea.ShipSailing.SailingShare"/> · <see cref="Sea.Sighting.Reach"/>).
        /// </summary>
        public int SailShare { get; set; } = 50;
        public int WatchShare { get; set; } = 25;

        /// <summary>몫을 고친다 — 둘 다 0 ~ 100 이고 합이 100 을 넘지 않게 자른다.</summary>
        public void SetShares(int sail, int watch)
        {
            SailShare = Math.Clamp(sail, 0, 100);
            WatchShare = Math.Clamp(watch, 0, 100 - SailShare);
        }

        /// <summary>선수상 1 ~ 10 — 함대 칸 곁 +0x1D(0 은 없음, <see cref="Town.Shipyard.Figureheads"/>).</summary>
        public int Figurehead { get; set; }

        /// <summary>선장 사람 번호 — 함대 칸 곁 +0x1B. -1 은 주인공.</summary>
        public int Captain { get; set; } = -1;

        public int Vacancy => Math.Max(0, MaxCrew - Crew);
    }

    public int Gold { get; private set; }
    public List<Ship> Ships { get; } = [];

    /// <summary>
    /// 도크에 맡긴 배 — 원본은 배 칸 10 ~ 39(서른)를 쓰고 +0x1B 에 「항구 | 0x80」 을 적는다(<c>0x00423A90</c>).
    /// 한 항구에 다섯까지다.
    /// </summary>
    public List<(int Port, Ship Ship)> Docked { get; } = [];

    public const int DockPerPort = 5, DockTotal = 30;

    public List<Ship> DockedAt(int port) => [.. Docked.Where(d => d.Port == port).Select(d => d.Ship)];

    /// <summary>그 항구 도크에 더 맡길 수 있는 척 수(<c>0x0049A1A0</c> − 맡긴 수).</summary>
    public int DockRoom(int port) =>
        Math.Max(0, Math.Min(DockPerPort, DockedAt(port).Count + DockTotal - Docked.Count) - DockedAt(port).Count);

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
    /// 먹을 때마다 사기가 움직인다(<see cref="MoraleChange"/>) — <paramref name="lead"/> 는 배 차례 → 그 배 선장 능력치 +0x1C.
    /// 아무 배에도 없으면 먹지 못하고 사기를 <c>min(16, 사기)</c> 깎는다(<c>0x004176B0</c>).
    /// 사기가 20 아래면 선원이 달아난다(<see cref="Deserters"/>). 아무 배에도 없던 것의 수를 돌려준다.
    /// </summary>
    public int ConsumeDay(Func<int, int>? lead = null)
    {
        int lacking = 0;
        foreach (int kind in (int[])[Food, Water])
            for (int i = 0; i < Ships.Count; i++)
            {
                var ship = Ships[i];
                int rate = kind == Food ? FoodRate : WaterRate;
                int need = DailyUse(ship.Crew, rate);
                var from = ship.Supplies[kind] > 0 ? ship : Ships.FirstOrDefault(s => s.Supplies[kind] > 0);
                if (from == null)
                {
                    lacking++;
                    ship.Morale -= Math.Min(16, ship.Morale);
                }
                else
                {
                    from.Supplies[kind] -= Math.Min(need, from.Supplies[kind]);
                    ship.Morale = Math.Clamp(ship.Morale - MoraleChange(lead?.Invoke(i) ?? 0, rate), 0, 100);
                }
                if (ship.Morale < MutinyBelow) ship.Crew -= Deserters(ship.Crew, ship.Morale);
            }
        return lacking;
    }

    /// <summary>달아나는 선원 — <c>min(선원, (20 − 사기) x 선원 / 100 + 1)</c>(<c>0x00417650</c>).</summary>
    public static int Deserters(int crew, int morale) => Math.Min(crew, (MutinyBelow - morale) * crew / 100 + 1);

    /// <summary>
    /// 한 번 먹을 때 깎이는 사기(<c>0x004175ED</c>) — 괄호 = 100 − 선장 능력치 / 5 − 비율, 괄호 &gt; 0 이면 괄호 / 5,
    /// 아니면 괄호 / 10(음수라 사기가 오른다).
    /// </summary>
    public static int MoraleChange(int lead, int rate)
    {
        int b = 100 - lead / 5 - rate;
        return b > 0 ? b / 5 : b / 10;
    }

    /// <summary>쥐 떼 · 괴혈병(<c>0x0049F598</c> 비트 0x10 · 0x20) — 항구에 들면 없어진다(<c>0x0041A588</c>).</summary>
    public bool Rats { get; set; }
    public bool Scurvy { get; set; }

    /// <summary>
    /// 바다의 하루 끝 병 셈(<c>0x00417970</c> · <c>0x00417AD0</c>). <paramref name="days"/> 는 바다에 나온 날 수,
    /// <paramref name="luck"/> · <paramref name="navLevel"/> 은 주인공 운(+0x23) · 항해 레벨(+0x24),
    /// <paramref name="lead2"/> 는 배 차례 → 그 배 선장 능력치 +0x1E. 새로 걸린 병 이름을 돌려준다(없으면 null).
    /// <code>
    ///   쥐 떼   30 일 넘어, 고양이가 없고 주사위(항해 레벨 / 5 + 2) == 0 이고 주사위(105) &gt; 운이면 인다(말 0x1D9).
    ///           있는 동안 배마다 식량 −10. 쥐약(0x29)이 없앤다
    ///   괴혈병  60 일 넘어, 주사위(105) &gt; 운이면 걸린다(말 0x1DA). 걸린 동안 배마다 선원 −min(선원, (100 − 선장 +0x1E) / 5 + 주사위(2)).
    ///           라임 주스(0x2B)가 낫게 한다
    /// </code>
    /// 고양이(아이템 0x19)를 지니고 나가면 <c>0x0046EED4</c> 에 0x40 이 서서(<c>0x00435A5C</c>) 쥐 떼가 일지 않는다.
    /// </summary>
    public string? DiseaseDay(int days, int luck, int navLevel, Func<int, int> lead2, Func<int, int> rand, bool cat = false)
    {
        string? caught = null;
        if (!Rats && !cat && days > 30 && rand(navLevel / 5 + 2) == 0 && rand(105) > luck) { Rats = true; caught = "쥐 떼"; }
        if (Rats)
            foreach (var ship in Ships) ship.Supplies[Food] -= Math.Min(ship.Supplies[Food], 10);
        if (!Scurvy && days > 60 && rand(105) > luck) { Scurvy = true; caught ??= "괴혈병"; }
        if (Scurvy)
            for (int i = 0; i < Ships.Count; i++)
                Ships[i].Crew -= Math.Min(Ships[i].Crew, (100 - lead2(i)) / 5 + rand(2));
        return caught;
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
            // 새 선원만큼 사기를 60 쪽으로 — 60 + (사기 − 60) x 전 선원 / 새 선원(0x00424241).
            int before = ship.Crew;
            ship.Crew += n;
            ship.Morale = DefaultMorale + (ship.Morale - DefaultMorale) * before / ship.Crew;
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

    /// <summary>값 없이 싣는다 — 시나리오 E2(<c>0x0043D980</c>). 빈 자리를 넘는 것은 버린다. 실은 양을 돌려준다.</summary>
    public int Stow(int good, int amount)
    {
        int gold = Gold;
        Gold = int.MaxValue / 2;
        int got = Buy(good, amount, 1);
        Gold = gold;
        return got;
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

    /// <summary>
    /// 「적하편성」 — <paramref name="from"/> 의 짐 하나를 <paramref name="to"/> 로 옮긴다. <paramref name="what"/> 은 0 ~ 3 이 보급
    /// (물 · 식량 · 자재 · 탄약), 4 부터 교역품 칸이다. 받는 배의 빈 자리 · 교역품 칸까지만 옮기고 옮긴 양을 돌려준다.
    /// </summary>
    public static int Transship(Ship from, Ship to, int what, int amount)
    {
        amount = Math.Min(amount, to.Free);
        if (what < SupplyKinds)
        {
            amount = Math.Clamp(amount, 0, from.Supplies[what]);
            from.Supplies[what] -= amount;
            to.Supplies[what] += amount;
            return amount;
        }
        int slot = what - SupplyKinds;
        if (slot >= CargoSlots || from.Cargo[slot].Good < 0) return 0;
        var (good, have) = from.Cargo[slot];
        int into = Array.FindIndex(to.Cargo, c => c.Good == good);
        if (into < 0) into = Array.FindIndex(to.Cargo, c => c.Good < 0);
        if (into < 0) return 0;
        amount = Math.Clamp(amount, 0, have);
        to.Cargo[into] = (good, to.Cargo[into].Amount + amount);
        from.Cargo[slot] = have - amount > 0 ? (good, have - amount) : (-1, 0);
        return amount;
    }

    /// <summary>
    /// 「승무원편성」(<c>0x00446310</c>) — 배에서 내린 선원은 함대 공용(<c>0x0049FAEC</c>)으로 모인다. 배 하나의 선원을
    /// <paramref name="crew"/> 로 맞추고(그 배 최대 · 지금 + 공용까지), 더 태우면 사기는 사람 수로 섞는다. 공용에 남은 수를 돌려준다.
    /// </summary>
    public (int Pool, int PoolMorale) Recrew(Ship ship, int crew, int pool, int poolMorale)
    {
        crew = Math.Clamp(crew, 0, Math.Min(ship.MaxCrew, ship.Crew + pool));
        int diff = crew - ship.Crew;
        if (diff > 0)
        {
            ship.Morale = (ship.Crew * ship.Morale + diff * poolMorale) / crew;
            pool -= diff;
        }
        else if (diff < 0)
        {
            int off = -diff;
            poolMorale = pool + off > 0 ? (pool * poolMorale + off * ship.Morale) / (pool + off) : ship.Morale;
            pool += off;
        }
        ship.Crew = crew;
        return (pool, poolMorale);
    }

    /// <summary>
    /// 뭍 「응급수리」(<c>0x0041B670</c>) — 자재 하나가 5 를 고친다. <paramref name="part"/> 는 0 내구 · 1 역풍 · 2 돛,
    /// 쓸 수 있는 자재는 min(함대 자재, (상한 − 지금 + 4) / 5). 쓴 자재는 앞 배부터 덜어 낸다(<c>0x0041B810</c>).
    /// </summary>
    public const int PatchPerMaterial = 5, Material = 2;

    public static int PatchNeed(int now, int max) => Math.Max(0, (max - now + PatchPerMaterial - 1) / PatchPerMaterial);

    public int Materials => Ships.Sum(s => s.Supplies[Material]);

    public void Patch(Ship ship, int part, int materials, int maxUpwind, int maxSail)
    {
        int add = materials * PatchPerMaterial;
        switch (part)
        {
            case 0: ship.Hull = Math.Min(ship.MaxHull, ship.Hull + add); break;
            case 1: ship.Upwind = Math.Min(maxUpwind, ship.Upwind + add); break;
            default: ship.Sail = Math.Min(maxSail, ship.Sail + add); break;
        }
        foreach (var s in Ships)
        {
            int k = Math.Min(materials, s.Supplies[Material]);
            s.Supplies[Material] -= k;
            materials -= k;
        }
    }
}
