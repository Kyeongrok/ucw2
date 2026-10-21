using Uw2.Game.Engine.Town;
using Xunit;

namespace Uw2.Tests;

/// <summary>마을 사람(<see cref="Townsfolk"/>) — 0x004361A0 · 0x00432C80 · 0x00432F20.</summary>
public class TownsfolkTests
{
    private static (int X, int Y)?[] Doors() =>
        [(10, 10), (20, 10), (30, 10), (40, 40), (50, 10), null, null, null, null, null, null, null];

    [Fact]
    public void Spawn_puts_walkers_and_standers_by_doors()
    {
        var t = new Townsfolk();
        t.Spawn(Doors(), banned: false);
        Assert.Equal(new Townsfolk.Person(8, 11, 2), t.People[0]);
        Assert.Equal(new Townsfolk.Person(30, 10, 2), t.People[2]);   // 조선소의 걷는 이는 문 자리
        Assert.Equal(new Townsfolk.Person(12, 11, 0), t.People[4]);
        Assert.False(t.People[6].Here);
        Assert.Equal(new Townsfolk.Person(52, 11, 3), t.People[7]);
        Assert.Equal(5, t.Kind(4));
        Assert.Equal(8, t.Kind(7));
    }

    [Fact]
    public void Banned_port_posts_three_guards_at_the_harbour()
    {
        var t = new Townsfolk();
        t.Spawn(Doors(), banned: true);
        Assert.Equal(new Townsfolk.Person(38, 42, 2), t.People[4]);
        Assert.Equal(new Townsfolk.Person(40, 43, 2), t.People[5]);
        Assert.Equal(new Townsfolk.Person(42, 42, 2), t.People[6]);
        Assert.Equal(7, t.Kind(4));
        Assert.Equal(5, t.At(41, 43));
    }

    [Fact]
    public void Walkers_never_step_onto_blocked_cells()
    {
        var t = new Townsfolk();
        t.Spawn(Doors(), banned: false);
        var rng = new Random(5);
        for (int i = 0; i < 2000; i++)
        {
            t.Step((x, y) => x is >= 5 and <= 55 && y is >= 5 and <= 15, (90, 90), rng.Next);
            foreach (var p in t.People.Take(Townsfolk.Walkers))
                Assert.True(p.X is >= 5 and <= 55 && p.Y is >= 5 and <= 15);
        }
    }
}
