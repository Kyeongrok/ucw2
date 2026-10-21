using Uw2.Game.Engine;
using Xunit;

namespace Uw2.Tests;

/// <summary>나라 셈(<see cref="Nations"/>) — 0x0042B980 · 0x0042BB60 · 0x0042BCB0.</summary>
public class NationsTests
{
    [Fact]
    public void Power_sums_home_region_and_strong_regions()
    {
        var ports = new List<(int, bool, int, int, int)>
        {
            (0, false, 400, 100, 0),   // 갈래 0 — 공업과 상관없이 경제 / 4
            (0, true, 300, 100, 0),    // 비트 8 항구는 경제 두 배
            (0, false, 600, 400, 5),   // 갈래 5 — 공업 300 넘어 경제 / 1
            (0, false, 900, 100, 6),   // 갈래 6 — 공업 모자라 빠진다
            (1, false, 800, 0, 0),
        };
        var p = Nations.Powers(ports);
        Assert.Equal((400 + 600) / 4 + 600, p[0]);
        Assert.Equal(800 / 4, p[1]);
        Assert.Equal((p[0] + p[1]) / 6, p[Nations.Pirates]);
    }

    [Fact]
    public void Regions_one_two_stop_at_first_weak()
    {
        var ports = new List<(int, bool, int, int, int)> { (2, false, 300, 0, 1), (2, false, 300, 500, 2) };
        Assert.Equal(0, Nations.Powers(ports)[2]);   // 갈래 1 이 약하면 갈래 2 도 안 센다
    }

    private static Func<int, int, int> Table(int[] power, int agg = 50, int rel = 50) =>
        (n, f) => f == Nations.PowerField ? power[n] : f == Nations.AggressionField ? agg : f >= Nations.RelationField ? rel : 0;

    [Fact]
    public void Target_is_weakest_weighted_unless_hero_hated()
    {
        int[] power = [500, 100, 300, 400, 200, 600, 50];
        Assert.Equal(6, Nations.Target(0, Table(power), 100, n => 0));            // 해적이 가장 약하다
        Assert.Equal(Nations.HeroTarget, Nations.Target(0, Table(power), 0, n => 0));   // |200 − 0| = 200 > 100
        Assert.Equal(5, Nations.Target(Nations.Pirates, Table(power), 100, n => 0));     // 해적은 (100 − 사이) x 세기가 큰 나라
    }

    [Fact]
    public void Policy_war_or_trade()
    {
        int[] power = [100, 300, 0, 0, 0, 0, 0];
        Func<int, int, int> t = (n, f) => f == Nations.TargetField ? 1 : Table(power, agg: 60)(n, f);
        Assert.Equal(4, Nations.Policy(0, t, 0, n => 0));        // 노리는 나라가 더 세다
        Assert.InRange(Nations.Policy(0, t, 0, n => n - 1), 0, 2);   // rand(120) = 119 ≥ 호전성 → 장사
        Assert.Equal(8, Nations.Policy(Nations.Pirates, t, 0, n => 0));
    }

    [Fact]
    public void Merchant_port_by_policy()
    {
        int[] owner = [.. Enumerable.Range(0, 100).Select(p => p < 10 ? 0 : p < 20 ? 1 : 6)];
        Func<int, int, int> read(int policy) => (n, f) => f == Nations.PolicyField ? policy : f == 0x0A ? (n == 0 ? 0 : 10) : 0;
        Assert.InRange(Nations.MerchantPort(0, read(1), p => owner[p], 1, n => 0), 1, 9);     // 제 나라 항구, 본거지 0 말고
        Assert.InRange(Nations.MerchantPort(0, read(0), p => owner[p], 1, n => 0), 20, 99);   // 임자 없는 항구
        Assert.InRange(Nations.MerchantPort(0, read(2), p => owner[p], 1, n => 0), 75, 92);
        Assert.InRange(Nations.MerchantPort(0, read(3), p => owner[p], 1, n => 0), 11, 19);   // 주인공 나라(1) 항구, 본거지 10 말고
    }

    private static Uw2.Support.Local.Formats.Economy OnePort(int economy, int industry, byte[] shares)
    {
        var state = new byte[Uw2.Support.Local.Formats.Economy.StateSize * 2];
        state[0] = (byte)economy; state[1] = (byte)(economy >> 8);
        state[4] = (byte)industry; state[5] = (byte)(industry >> 8);
        for (int k = 0; k < 6; k++) state[8 + k] = shares[k];
        for (int i = 0; i < 10; i++) state[0x0E + i] = 50;
        var market = new byte[Uw2.Support.Local.Formats.Economy.MarketSize];
        for (int g = 0; g < 46; g++) { market[g * 2] = 100; }
        return Uw2.Support.Local.Formats.Economy.FromParts([.. Enumerable.Range(0, 46).Select(g => $"g{g}")], market, state);
    }

    [Fact]
    public void Npc_invest_raises_share_and_takes_round_robin()
    {
        var e = OnePort(100, 300, [10, 0, 5, 0, 0, 0]);
        int left = e.NpcInvest(0, 1, 20);   // 20 / 2 x 100 = 1000 을 경제 투자에(경제 < 공업), 돈 10 남고 몫 +6
        Assert.Equal(10, left);
        Assert.Equal(1000, e.StateBytes[2] | e.StateBytes[3] << 8);
        Assert.Equal(6, e.StateBytes[8 + 1]);
        Assert.Equal(15 - 6, e.StateBytes[8] + e.StateBytes[8 + 2]);
    }

    [Fact]
    public void Npc_buy_and_sell_move_price_index()
    {
        var e = OnePort(100, 100, [0, 0, 0, 0, 0, 0]);
        var bought = e.NpcBuy(0, 12, 50, n => 0);   // 값 (50 + 50) x 100 / 100 = 100, 양 min(1000, 5000 / 100) = 50
        Assert.Equal((50, 50), bought);
        int cat = Uw2.Support.Local.Formats.Economy.CategoryOf(12);
        Assert.Equal(53, e.StateBytes[0x0E + cat]);
        e.NpcSell(0, 12, n => 0);
        Assert.Equal(51, e.StateBytes[0x0E + cat]);
    }

    [Fact]
    public void Aid_accrues_with_favour()
    {
        var m = new ScenarioMemory();
        m.Write(ScenarioMemory.At(ScenarioMemory.HeroTable, 0, ScenarioMemory.RankField), 2, false);
        m.Write(ScenarioMemory.At(ScenarioMemory.HeroTable, 0, 6 + 1), 150, false);   // 에스파니아 호감 150
        m.Write(ScenarioMemory.At(ScenarioMemory.HeroTable, 0, 6 + 2), 50, false);    // 오스만 호감 50
        m.Write(ScenarioMemory.At(ScenarioMemory.NationTable, 1, 0), 9000, true);
        m.Write(ScenarioMemory.At(ScenarioMemory.NationTable, 2, 8), 3, false);
        Nations.Aid(m, 0);
        int F(int n, int f) => m.Read(ScenarioMemory.At(ScenarioMemory.NationTable, n, f), false);
        Assert.Equal((6, 18, 4, 4), (F(1, 5), F(1, 6), F(1, 7), F(1, 8)));   // k = 18000
        Assert.Equal((0, 0xFF, 0), (F(2, 5), F(2, 7), F(2, 8)));
    }
}
