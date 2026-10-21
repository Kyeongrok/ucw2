using Uw2.Game.Engine;
using Uw2.Game.Engine.Town;
using Xunit;

namespace Uw2.Tests;

/// <summary>기항 항해 경험(<see cref="Navigators.SailExperience"/>) — 0x0041A4C0 · 0x0041A7C0.</summary>
public class SailExperienceTests
{
    [Fact]
    public void Experience_rolls_levels_and_raises_stats()
    {
        var m = new ScenarioMemory();
        Navigators.Set(m, 5, Navigators.NavLevel, 2);
        for (int k = 0; k < 4; k++) Navigators.Set(m, 5, Navigators.Stats + k, 50);
        // 레벨 2 → 3 에 120, 3 → 4 에 270 — 400 이면 둘 오르고 10 이 남는다.
        var (ups, raised) = Navigators.SailExperience(m, 5, 400, _ => 2);
        Assert.Equal(2, ups);
        Assert.Equal(4, Navigators.Get(m, 5, Navigators.NavLevel));
        Assert.Equal(10, m.Read(ScenarioMemory.At(ScenarioMemory.PeopleTable, 5, Navigators.NavExp), true));
        Assert.Equal([0, 1, 2], raised);
        Assert.Equal(54, Navigators.Get(m, 5, Navigators.Stats));       // 두 번 +2
        Assert.Equal(52, Navigators.Get(m, 5, Navigators.Stats + 3));   // 한 번만
    }

    [Fact]
    public void Below_need_only_adds_experience()
    {
        var m = new ScenarioMemory();
        Navigators.Set(m, 1, Navigators.NavLevel, 10);
        var (ups, raised) = Navigators.SailExperience(m, 1, 100, _ => 2);
        Assert.Equal(0, ups);
        Assert.Empty(raised);
        Assert.Equal(100, m.Read(ScenarioMemory.At(ScenarioMemory.PeopleTable, 1, Navigators.NavExp), true));
    }
}
