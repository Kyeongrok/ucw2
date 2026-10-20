using Uw2.Game.Engine;
using Uw2.Game.Engine.Sea;
using Uw2.Support.Local.Formats;
using Xunit;

namespace Uw2.Tests;

/// <summary>원본 셈을 옮긴 것이 원본과 같은 값을 내는지 — 숫자는 EXE 를 손으로 따라 셈한 값이다.</summary>
public class EngineTests
{
    [Theory]
    // 0x00418E80 → 0x00436740: 비트 0..2 세기, 3..5 방위
    [InlineData(0x00, 0, 0)]
    [InlineData(0x0D, 1, 5)]
    [InlineData(0x3F, 7, 7)]
    [InlineData(0xC9, 1, 1)]
    public void WindByteSplits(byte b, int dir, int speed) =>
        Assert.Equal((dir, speed), WindCurTable.Split(b));

    [Fact]
    public void AngleWrapsByAbsoluteDifference()
    {
        Assert.Equal(0, Sailing.AngleOf(3, 3));
        Assert.Equal(4, Sailing.AngleOf(0, 4));
        Assert.Equal(7, Sailing.AngleOf(7, 0));       // 표 7줄이 1줄과 같아 둘레가 맞는다
        Assert.True(Sailing.IsHeadwind(3) && Sailing.IsHeadwind(5) && !Sailing.IsHeadwind(2));
    }

    [Fact]
    public void PlayerShipSpeedFollowsThe0x00448520Steps()
    {
        // 바르샤(0번): 돛 80 · 역풍 70 · 드는 선원 5 · 짐칸 50 · 돛 갈래 4. 조안: 항해술 75 · 레벨 1.
        var ship = new ShipSailing(4, 80, 70, false, 10, 100, 5, 0, 50, 1, 75);
        // 순풍(각 0) 풍속 4: 9*80*4/150 = 19 → 짐 0 이면 150% → 28 → +2 = 30 → *75/75 = 30
        Assert.Equal(30, Sailing.ShipSpeed(ship, 0, 4));
        // 정면 역풍(각 4) 풍속 4: 7*80*4/150 = 14 → 21 → 23 → 23 → *70/100 = 16
        Assert.Equal(16, Sailing.ShipSpeed(ship, 4, 4));
        // 짐이 꽉 차면 80%
        var full = ship with { Load = 50 };
        Assert.Equal(16, Sailing.ShipSpeed(full, 0, 4));   // 19 → 15 → 16 → 16
    }

    [Fact]
    public void FleetSpeedHasFloorAndCalm()
    {
        var ship = new ShipSailing(4, 80, 70, false, 10, 100, 5, 0, 50, 1, 75);
        Assert.Equal(0, Sailing.FleetSpeed([ship], 0, 0, 0));          // 바람이 자고 노가 없으면 선다
        Assert.Equal(Sailing.MinSpeed, Sailing.FleetSpeed([ship], 4, 0, 1));
        Assert.Equal(15, Sailing.Knots(30));
    }

    [Fact]
    public void DateRollsLikeTheOriginal()
    {
        var d = GameDate.NewGame;
        Assert.Equal("1522년 May 17일", d.ToString());
        d = new GameDate(21, 1, 27);                    // 2월 28일
        d.NextDay();
        Assert.Equal((21, 2, 0), (d.Year, d.Month, d.Day));      // 윤년 셈이 뒤집혀 2월은 늘 28일
        d = new GameDate(21, 11, 30);
        d.NextDay();
        Assert.Equal((22, 0, 0), (d.Year, d.Month, d.Day));
        Assert.True(new GameDate(GameDate.LastYear, 11, 30).NextDay());
    }

    [Fact]
    public void OddLeapOnlyOnCenturiesNotDivisibleBy400()
    {
        Assert.False(GameDate.OddLeap(24));    // 1524 — 진짜 윤년이지만 원본은 아니라고 한다
        Assert.True(GameDate.OddLeap(0));      // 1500
    }

    [Theory]
    [InlineData(0, 11, false)]
    [InlineData(0, 12, true)]
    [InlineData(0, 60, false)]
    [InlineData(1, 12, false)]
    [InlineData(1, 30, true)]
    [InlineData(9, 7, true)]
    [InlineData(9, 10, false)]
    [InlineData(11, 47, false)]
    [InlineData(11, 48, true)]
    [InlineData(3, 0, true)]
    public void BuildingHours(int building, int tick, bool open) =>
        Assert.Equal(open, PortDoors.IsOpen(building, tick));

    [Fact]
    public void WalkBoundsRule()
    {
        var b = new WalkBounds(29, 34, 40);           // 리스본 벌
        Assert.True(b.CanStand(0, 0));
        Assert.True(b.CanStand(30, 10));              // 왼발 a..b-1 은 된다
        Assert.False(b.CanStand(10, 30));             // 오른발 a..b-1 은 안 된다
        Assert.True(b.CanStand(10, 35));              // 오른발 b..c-1 은 된다
        Assert.False(b.CanStand(35, 10));             // 왼발 b 이상은 안 된다
        Assert.False(b.CanStand(10, 40));
    }

    [Fact]
    public void KaoDecodesInterleavedPlanes()
    {
        // 첫 토막의 면 셋이 0xFF, 0x00, 0xFF 면 여덟 점이 다 색인 5(면0 | 면2).
        var data = new byte[KaoImages.FindBytes];
        data[0] = 0xFF; data[2] = 0xFF;
        var px = KaoImages.Decode(data, KaoImages.FindW, KaoImages.FindH);
        Assert.All(px.Take(8), v => Assert.Equal(5, v));
        Assert.Equal(0, px[8]);
    }

    [Fact]
    public void WeatherCalmPinsWindToZero()
    {
        var w = new Weather();
        Assert.Equal(5, w.Apply(5));
    }
}
