using Uw2.Game.Engine.Sea;
using Xunit;

namespace Uw2.Tests;

/// <summary>해전 셈(<see cref="Battle"/>) — docs/27 의 원본 손과 맞는지.</summary>
public class BattleTests
{
    private static Battle.Captain Cap(int lead = 60, int sea = 60, int sword = 60, int luck = 50, int lv = 10) =>
        new() { Person = 0, Stats = [lead, sea, 50, 50, 50, sword, 50, luck], NavLevel = lv, BattleLevel = lv };

    private static Battle OpenSea(Func<int, int>? rand = null)
    {
        var b = new Battle(rand ?? (n => 0));
        b.BuildMap((_, _) => true);
        b.SetWind(4, 0);
        return b;
    }

    private static Battle.Ship Add(Battle b, int side, int x, int y, int heading = 0, bool player = false, int gun = 4) =>
        Place(b.Add(side, Cap(), "배", 100, 60, 60, 60, 60, gun, 20, 50, 50, player, true, false, 60, 100), x, y, heading);

    private static Battle.Ship Place(Battle.Ship s, int x, int y, int heading)
    {
        s.X = x; s.Y = y; s.Heading = heading;
        return s;
    }

    [Theory]
    [InlineData(10, 10, 10, 11, 1)]
    [InlineData(10, 10, 11, 10, 1)]   // 짝수 열 → 홀수 열은 반 칸 아래라 같은 줄이 오른아래
    [InlineData(10, 10, 11, 9, 1)]
    [InlineData(10, 10, 12, 10, 2)]
    [InlineData(10, 10, 10, 15, 5)]
    [InlineData(10, 10, 14, 10, 4)]
    public void Distance_matches_0x004112B0(int x1, int y1, int x2, int y2, int d) =>
        Assert.Equal(d, Battle.Distance(x1, y1, x2, y2));

    [Fact]
    public void Neighbors_are_distance_one()
    {
        foreach (int x in (int[])[10, 11])
            for (int dir = 0; dir < 6; dir++)
            {
                var (nx, ny) = Battle.Step(x, 10, dir);
                Assert.Equal(1, Battle.Distance(x, 10, nx, ny));
            }
    }

    [Fact]
    public void Broadside_only_for_north_heading()
    {
        // 위를 보는 배(뱃머리 0)는 옆(dY ≤ dx)만 쏜다 — 바로 앞은 못 쏜다.
        Assert.True(Battle.InArc(20, 20, 0, 5, 23, 20));
        Assert.False(Battle.InArc(20, 20, 0, 5, 20, 17));
        Assert.False(Battle.InArc(20, 20, 0, 5, 21, 20));   // 거리 1 은 백병 자리
    }

    [Fact]
    public void Wind_costs_one_to_four_from_wind_direction()
    {
        var b = OpenSea();
        b.SetWind(3, 2);
        var s = Add(b, 0, 20, 20, 2);
        // 곧게: 바람값 x 돛 / 100 + 5 — 바람 갈래(2)는 1, 맞은편(5)은 4.
        Assert.Equal(1 * 60 / 100 + 5, b.Cost(s, 2, 2));
        Assert.Equal(4 * 60 / 100 + 5, b.Cost(s, 5, 5));
        // 꺾을 때 바람값 3 이상이면 10 − 역풍 x 5 / 100.
        Assert.Equal(10 - 60 * 5 / 100, b.Cost(s, 4, 3));
        Assert.Equal(5, b.Cost(s, 3, 2));
    }

    [Fact]
    public void Reach_goes_straight_when_momentum_low()
    {
        var b = OpenSea();
        var s = Add(b, 0, 20, 20, 0);
        s.Captain.Stats[1] = 10;   // 항해술이 낮으면 쌓임이 50 에 못 미친다
        var routes = b.Reach(s);
        Assert.All(routes, r => Assert.All(r.Dirs, d => Assert.Equal(0, d)));
        Assert.Contains(routes, r => r.Dirs.Count > 0);
    }

    [Fact]
    public void Steps_are_ten_to_twenty_five()
    {
        var b = OpenSea();
        var s = Add(b, 0, 20, 20);
        Assert.InRange(b.Steps(s), 10, 25);
        s.Captain.Stats[1] = 0;
        Assert.Equal(10, b.Steps(s));
    }

    [Fact]
    public void Fire_uses_shot_and_hurts()
    {
        var b = OpenSea(n => n - 1);
        var a = Add(b, 0, 20, 20, 0);
        var t = Add(b, 1, 23, 20, 0);
        var hit = b.Fire(a, t);
        Assert.Equal(49, a.Shot);
        Assert.True(hit.Damage >= 1);
        Assert.Equal(60 - hit.Hull, t.Hull);
        Assert.Equal(100 - hit.Crew, t.Crew);
    }

    [Fact]
    public void Board_trades_crew()
    {
        var b = OpenSea(n => n / 2);
        var a = Add(b, 0, 20, 20);
        var t = Add(b, 1, 20, 21);
        var hit = b.Board(a, t);
        Assert.InRange(hit.Crew, 1, 50);
        Assert.InRange(hit.Damage, 1, 50);
    }

    [Fact]
    public void Night_ends_battle()
    {
        var b = OpenSea();
        Add(b, 0, 10, 10);
        Add(b, 1, 40, 40);
        b.Time = Battle.Night - 1;
        int guard = 0;
        while (b.NextShip() is { } s && guard++ < 1000) b.AiTurn(s);
        Assert.Equal(Battle.Outcome.Sunset, b.Result);
    }

    [Fact]
    public void Sinking_enemy_flagship_wins()
    {
        var b = OpenSea();
        Add(b, 0, 10, 10, player: true);
        var e = Add(b, 1, 40, 40);
        e.Hull = 0;
        Assert.True(b.CheckEnd());
        Assert.Equal(Battle.Outcome.EnemySunk, b.Result);
    }

    [Fact]
    public void Own_flagship_retreat_is_three()
    {
        var b = OpenSea();
        var p = Add(b, 1, 10, 10, player: true);
        Add(b, 0, 40, 40);
        b.Leave(p);
        Assert.True(b.CheckEnd());
        Assert.Equal(Battle.Outcome.PlayerLeft, b.Result);
    }

    [Fact]
    public void Land_shrinks_four_cells_toward_sea()
    {
        var b = new Battle(n => 0);
        b.BuildMap((cx, cy) => !(cx is 2 or 3 && cy is 2 or 3));
        // 뭍 칩 2 x 2 는 열 18 ~ 33 · 줄 16 ~ 31 인데 바다 쪽 네 칸씩 깎여 열 22 ~ 29 · 줄 20 ~ 27 만 남는다.
        Assert.True(b.IsLand(25, 24));
        Assert.True(b.IsLand(22, 20));
        Assert.False(b.IsLand(21, 24));
        Assert.False(b.IsLand(19, 17));
    }

    [Fact]
    public void Ai_battle_runs_to_an_end()
    {
        var rng = new Random(7);
        var b = new Battle(n => n < 2 ? 0 : rng.Next(n));
        b.BuildMap((cx, cy) => !(cx == 0 && cy == 0));
        b.RollWind();
        for (int i = 0; i < 4; i++)
        {
            b.Add(0, Cap(), $"우리{i}", 80, 60, 60, 60, 60, 1 + i, 20, 30, 50, i == 0, true, false, 60, 80);
            b.Add(1, Cap(), $"적{i}", 80, 60, 60, 60, 60, 4, 20, 30, 50, false, i % 2 == 0, false, 60, 80);
        }
        b.Deploy(0, 2, -1, 0);
        b.Deploy(1, 6, 0, 0);
        Assert.All(b.All, s => Assert.False(b.IsLand(s.X, s.Y)));
        b.Time = 15;
        int turns = 0;
        while (b.NextShip() is { } s && turns++ < 100_000) b.AiTurn(s);
        Assert.NotEqual(0, b.Result);
        Assert.True(turns < 100_000);
    }

    [Fact]
    public void Order_flips_offense_between_zero_and_three()
    {
        var b = OpenSea();
        var s = Add(b, 0, 10, 10);
        s.Mode = 3;
        Battle.Order(s, 0);
        Assert.Equal(0, s.Mode);
        Battle.Order(s, 0);
        Assert.Equal(3, s.Mode);
        bool g = s.Gunner;
        Battle.Order(s, 1);
        Assert.Equal(!g, s.Gunner);
    }

    [Theory]
    [InlineData(0, 1, 1)]   // 상선은 투자
    [InlineData(2, 3, 2)]   // 방침 2 상선은 사들이기
    [InlineData(8, 5, 4)]   // 방침 8 해군은 무차별
    [InlineData(7, 2, 6)]   // 방침 7 상선도 노린다
    [InlineData(4, 9, 6)]
    [InlineData(3, 0, 6)]
    public void Npc_policy_table_0x004540F0(int policy, int kind, int mode) =>
        Assert.Equal(mode, NpcFleet.PolicyMode(policy, kind));
}
