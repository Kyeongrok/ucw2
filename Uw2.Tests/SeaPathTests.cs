using Uw2.Game.Engine.Sea;
using Xunit;

namespace Uw2.Tests;

/// <summary>항이동 길 찾기(<see cref="SeaPath"/>) — 벽을 돌아가고 모서리를 끊지 않는다.</summary>
public class SeaPathTests
{
    [Fact]
    public void Goes_around_a_wall_without_cutting_corners()
    {
        // x = 50 에 y 0 ~ 59 벽, 60 아래로만 지나간다.
        bool Sea(int x, int y) => !(x == 50 && y < 60);
        var path = SeaPath.Find(Sea, (40, 10), (60, 10), reach: 0);
        Assert.NotNull(path);
        Assert.Equal((60, 10), path![^1]);
        Assert.All(path, p => Assert.True(Sea(p.X, p.Y)));
        Assert.Contains(path, p => p.Y >= 60);
        for (int i = 1; i < path.Count; i++)
            Assert.True(Math.Max(Math.Abs(path[i].X - path[i - 1].X), Math.Abs(path[i].Y - path[i - 1].Y)) == 1);
    }

    [Fact]
    public void Wraps_around_the_date_line()
    {
        var path = SeaPath.Find((_, _) => true, (2158, 100), (2, 100), reach: 0);
        Assert.NotNull(path);
        Assert.True(path!.Count <= 5);
    }

    [Fact]
    public void Unreachable_gives_null()
    {
        bool Sea(int x, int y) => !(Math.Abs(x - 100) <= 3 && Math.Abs(y - 100) <= 3) || (x == 100 && y == 100);
        Assert.Null(SeaPath.Find(Sea, (0, 0), (100, 100), reach: 0, maxExpand: 50_000));
    }
}
