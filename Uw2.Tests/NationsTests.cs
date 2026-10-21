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
}
