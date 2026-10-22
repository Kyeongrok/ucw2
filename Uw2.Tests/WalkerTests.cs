using Uw2.Game.Engine.Town;
using Uw2.Support.Local.Formats;
using Xunit;

namespace Uw2.Tests;

/// <summary>도시 걸음(<see cref="Walker"/>) — 원본처럼 한 칸씩(<c>0x00432FE0</c>).</summary>
public class WalkerTests
{
    private static Walker Open(params (int X, int Y)[] walls)
    {
        var cells = new byte[PortMap.Size * PortMap.Size];
        foreach (var (x, y) in walls) cells[y * PortMap.Size + x] = 50;
        var w = new Walker(cells, new WalkBounds(10, 10, 20));
        Assert.True(w.PlaceNear(40, 40));
        return w;
    }

    [Fact]
    public void Walks_one_whole_cell_per_step_then_waits()
    {
        var w = Open();
        double x = w.X;
        w.Step_(1, 0);
        Assert.Equal(x + 1, w.X);
        for (int i = 0; i < Walker.CellTicks - 1; i++) w.Step_(1, 0);
        Assert.Equal(x + 1, w.X);   // 칸 걸음 사이에는 그대로
        w.Step_(1, 0);
        Assert.Equal(x + 2, w.X);
        Assert.Equal(2, w.Walked);
    }

    [Fact]
    public void Diagonal_input_walks_sideways_only()
    {
        var w = Open();
        double x = w.X, y = w.Y;
        w.Step_(-1, 1);
        Assert.Equal((x - 1, y), (w.X, w.Y));
        Assert.Equal(Walker.Facing.Left, w.Face);
    }

    [Fact]
    public void Blocked_step_stays_put_without_sliding()
    {
        var w = Open();
        // 오른발이 밟을 칸(round(x)) 오른쪽을 막는다.
        int right = (int)System.Math.Round(w.X) + 1, row = (int)System.Math.Floor(w.Y);
        var walled = Open((right, row));
        double x = walled.X, y = walled.Y;
        walled.Step_(1, 0);
        Assert.True(walled.Blocked);
        Assert.Equal((x, y), (walled.X, walled.Y));
    }
}
