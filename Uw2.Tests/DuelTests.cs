using Uw2.Game.Engine.Sea;
using Xunit;

namespace Uw2.Tests;

/// <summary>일기토(<see cref="Duel"/>) — docs/27 의 0x004143F0 셈.</summary>
public class DuelTests
{
    private static Duel.Fighter F(int sword = 60, int lv = 10, int style = 1, int weapon = 20, int armor = 0) =>
        new("가", sword, lv, style, weapon, armor);

    [Fact]
    public void Even_turn_pulls_toward_challenger()
    {
        var d = new Duel(F(), F(), 100, n => n - 1);   // rand 는 늘 끝값 — 피하기 판정(rand(100) = 99)에 안 걸린다
        int dmg = d.Exchange(1, 0);
        Assert.True(dmg > 0);
        Assert.Equal(100 + dmg, d.Rope);
        int back = d.Exchange(1, 0);
        Assert.Equal(100 + dmg - back, d.Rope);
    }

    [Fact]
    public void Strength_matches_formula()
    {
        // 처음 쌓임은 1 이라 지난 공격 = 1. 힘 = 60 x 10 / 50 + 30 = 42, 세기 = (20 + 42) x (4 − 1) / 4 = 46 → x (0 + 50 + 20) / 100 = 32,
        // 피해 = min(50, 32 x (90 − 10 − 0) / 100) = 25, + rand(5) = 4 → 29.
        var d = new Duel(F(), F(), 100, n => n - 1);
        Assert.Equal(29, d.Exchange(0, 0));
    }

    [Fact]
    public void Dodge_roll_zeroes_damage()
    {
        var d = new Duel(F(), F(), 100, n => 0);
        Assert.Equal(0, d.Exchange(2, 2));
        Assert.Equal(100, d.Rope);
    }

    [Fact]
    public void Ends_at_twenty_turns_or_full_rope()
    {
        var d = new Duel(F(), F(), 195, n => n - 1);
        d.Exchange(0, 0);
        Assert.True(d.Over);
        Assert.Equal(Duel.Full, d.Rope);
    }

    [Fact]
    public void Npc_gear_by_levels()
    {
        Assert.Equal((0x0F, 0x11), Duel.NpcGear(0, 0));
        Assert.Equal((0x0D, 0x13), Duel.NpcGear(16, 20));
        Assert.Equal((0x0C, 0x13), Duel.NpcGear(30, 30));   // 24 면 항해 레벨 % 3 + 3
    }
}
