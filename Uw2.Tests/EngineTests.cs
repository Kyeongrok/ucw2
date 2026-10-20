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

    [Fact]
    public void PlayerBuysWithinGoldAndRoomThenSells()
    {
        var p = new PlayerState(1000);
        p.Ships.Add(new PlayerState.Ship { Type = 5, Name = "카라벨 라티나", Room = 80 });
        Assert.Equal(27, p.Buy(15, 100, 36));          // 돈 1000 / 36 = 27통
        Assert.Equal(1000 - 27 * 36, p.Gold);
        Assert.Equal(80 - 27, p.Free);
        Assert.Equal(27, p.SellAll(15, 50));
        Assert.Equal(1000 - 27 * 36 + 27 * 50, p.Gold);
        Assert.Equal(80, p.Free);
        Assert.Equal(3, p.BuySupply(0, 3, 0));         // 물은 공짜, 한 단 열 통
        Assert.Equal(30, p.Ships[0].Supplies[0]);
    }

    [Fact]
    public void CargoUsesAtMostFiveSlots()
    {
        var p = new PlayerState(100000);
        p.Ships.Add(new PlayerState.Ship { Type = 10, Name = "갤리온", Room = 1000 });
        for (int g = 0; g < 6; g++) p.Buy(g, 10, 1);
        Assert.Equal(5, p.CargoTotals().Count);         // 여섯째 교역품은 칸이 없다
    }

    [Fact]
    public void PubHiresByEconomyAndCharmThenFillsShipsInOrder()
    {
        // 경제 900 · 매력 90(몫 30): 올 사람 min(900 x 30 / 500 = 54, (0 + 1) x 30) = 30, 값 900 / 20 + 5 = 50
        Assert.Equal(50, Uw2.Game.Engine.Town.Pub.Price(900));
        Assert.Equal(30, Uw2.Game.Engine.Town.Pub.Available(900, 90, 0));
        Assert.Equal(54, Uw2.Game.Engine.Town.Pub.Available(900, 90, 1));
        var p = new PlayerState(1000);
        p.Ships.Add(new PlayerState.Ship { Type = 5, Name = "앞", Room = 80, MaxCrew = 20, Crew = 15 });
        p.Ships.Add(new PlayerState.Ship { Type = 5, Name = "뒤", Room = 80, MaxCrew = 20 });
        Assert.Equal(20, Uw2.Game.Engine.Town.Pub.MostHirable(30, p.Gold, 50, p.Vacancy));   // 돈 1000 / 50
        Assert.Equal(20, p.Hire(20, 50));
        Assert.Equal((20, 15), (p.Ships[0].Crew, p.Ships[1].Crew));
        Assert.Equal(0, p.Gold);
    }

    [Fact]
    public void BankKeepsOneSignedBalanceWithMonthlyInterest()
    {
        var p = new PlayerState(5000);
        Assert.Equal(5000, Uw2.Game.Engine.Town.Bank.MaxDeposit(p.Gold, p.Balance));
        Assert.Equal(0, Uw2.Game.Engine.Town.Bank.MaxDeposit(1000, 0));          // 1000 을 넘어야 한다
        p.Transfer(4000);
        Assert.Equal((1000, 4000), (p.Gold, p.Balance));
        Assert.Equal(0, Uw2.Game.Engine.Town.Bank.MaxLoan(p.Balance, 0));        // 예금이 있으면 못 빌린다
        p.NewMonth();
        Assert.Equal(4120, p.Balance);                                           // 3%
        p.Transfer(-4120);
        Assert.Equal(1000, Uw2.Game.Engine.Town.Bank.MaxLoan(p.Balance, 0));
        Assert.Equal(11000, Uw2.Game.Engine.Town.Bank.MaxLoan(p.Balance, 1));    // 1000 + 10000 x 1²
        p.Transfer(-1000);
        p.NewMonth();
        Assert.Equal(-1100, p.Balance);                                          // 빚 10%
        Assert.Equal(1100, Uw2.Game.Engine.Town.Bank.MaxRepay(p.Gold, p.Balance));
        Assert.Equal(1_000_000, Uw2.Game.Engine.Town.Bank.WithInterest(990_000));
    }

    [Fact]
    public void ShipyardRepairsToMaxHullAndSellsForHalfTheValue()
    {
        // 카라벨 라티나 꼴: 내구 30 · 역풍 90 · 돛 75 · 값 240
        var type = new StartData.ShipType(5, 0, 30, 90, 75, 0, 10, 120, 20, 5, 240);
        Assert.Equal(27, Uw2.Game.Engine.Town.Shipyard.NewHull(type));
        var ship = new PlayerState.Ship { Type = 5, Name = "배", Room = 80, Hull = 20, MaxHull = 27, Upwind = 88, Sail = 75 };
        Assert.Equal(20 * (2 + 0 + 7), Uw2.Game.Engine.Town.Shipyard.RepairCost(type, ship));
        Uw2.Game.Engine.Town.Shipyard.Repair(type, ship);
        Assert.Equal((27, 90), (ship.Hull, ship.Upwind));
        // 등급 2: (2 x 240 / 10 + 4 x 240 / 5) x (50 + 50) / 100 = 240 → 10 에서 자름 240, 판 값 120
        Assert.Equal(240, Uw2.Game.Engine.Town.Shipyard.Value(240, 2, 50));
        Assert.Equal(120, Uw2.Game.Engine.Town.Shipyard.SellPrice(240, 2, 50));
    }

    [Fact]
    public void PortsGrowFromInvestmentAndPriceIndexesDriftTowardFifty()
    {
        var states = new byte[Economy.StateSize];
        states[0] = 0xE8; states[1] = 0x03;          // 경제 1000
        states[2] = 0x2C; states[3] = 0x01;          // 투자 300
        states[4] = 100; states[6] = 0x58; states[7] = 0x02;   // 공업 100 · 투자 600
        for (int i = 0; i < 10; i++) states[0x0E + i] = (byte)(i < 5 ? 40 : 60);
        var eco = Economy.FromParts([], [], states);
        eco.NewMonth(_ => 2);                        // 주사위 늘 2 → 3 씩
        Assert.Equal(1000, eco.States[0].Economy);   // 1000 에서 자른다
        Assert.Equal(102, eco.States[0].Industry);
        Assert.Equal(43, eco.States[0].PriceIndex[0]);
        Assert.Equal(57, eco.States[0].PriceIndex[9]);
    }

    [Fact]
    public void InvestingRaisesOurShareAndOthersPayEqually()
    {
        var states = new byte[Economy.StateSize];
        states[0] = 100;                                        // 경제 100
        byte[] shares = [50, 30, 20, 0, 0, 0];
        shares.CopyTo(states, 8);
        var eco = Economy.FromParts([], [], states);
        Assert.Equal(50000, eco.MaxInvest(0, false, 90000));
        Assert.Equal(10, eco.Invest(0, false, 1000, nation: 0));  // 1000 / 100 = 10
        Assert.Equal([60, 25, 15, 0, 0, 0], eco.States[0].Shares);
        Assert.Equal(49000, eco.MaxInvest(0, false, 90000));
    }

    [Fact]
    public void ShipsEatFromTheirOwnHoldThenFromTheFirstThatHasIt()
    {
        var p = new PlayerState(0) { FoodRate = 50, WaterRate = 0 };   // 새 게임은 둘 다 100
        p.Ships.Add(new PlayerState.Ship { Type = 5, Name = "앞", Room = 200, Crew = 20 });
        p.Ships.Add(new PlayerState.Ship { Type = 5, Name = "뒤", Room = 200, Crew = 10 });
        p.Ships[0].Supplies[PlayerState.Food] = 30;
        p.Ships[0].Supplies[PlayerState.Water] = 5;
        Assert.Equal(0, p.ConsumeDay());
        // 앞 배: 식량 20 x 50 / 100 + 1 = 11, 물 1. 뒤 배는 제 것이 없어 앞 배에서 식량 6 · 물 1
        Assert.Equal(30 - 11 - 6, p.Ships[0].Supplies[PlayerState.Food]);
        Assert.Equal(5 - 2, p.Ships[0].Supplies[PlayerState.Water]);
        p.Ships[0].Supplies[PlayerState.Water] = 0;
        Assert.Equal(2, p.ConsumeDay());                     // 두 배 다 물이 없다
    }

    [Fact]
    public void BuildGradesGrowWithIndustry()
    {
        Assert.Equal(3, Uw2.Game.Engine.Town.Shipyard.GradeChoices(5, 400));
        Assert.Equal(4, Uw2.Game.Engine.Town.Shipyard.GradeChoices(5, 700));
        Assert.Equal(6, Uw2.Game.Engine.Town.Shipyard.GradeChoices(0x18, 0));
        Assert.Equal(192, Uw2.Game.Engine.Town.Shipyard.BuildPrice(240, 0));   // 4 x 240 / 5
        Assert.Equal(240, Uw2.Game.Engine.Town.Shipyard.BuildPrice(240, 2));
    }

    [Fact]
    public void MatesArePaidMonthlyAndLikingFollowsWageAndLeadership()
    {
        var m = new ScenarioMemory();
        for (int i = 0; i < Uw2.Game.Engine.Town.Navigators.MateSlots; i++)
            m.Write(new ScenarioMemory.Pointer(6, 3 + i), 0xFF, false);
        Uw2.Game.Engine.Town.Navigators.Set(m, 0, 0x1C, 60);              // 주인공 능력치 +0x1C
        Uw2.Game.Engine.Town.Navigators.Set(m, 90, 0x1C, 70);             // 동료의 가장 큰 능력치 70
        Uw2.Game.Engine.Town.Navigators.Set(m, 90, 0x2B, 40);
        Assert.True(Uw2.Game.Engine.Town.Navigators.Join(m, 90, 0, 50));
        int gold = 100;
        Assert.Equal(50, Uw2.Game.Engine.Town.Navigators.PayWages(m, 0, ref gold));
        Assert.Equal(50, gold);
        // 호감 40 + 10(들 때) + 60 + 5 x 2 − 70 = 50
        Assert.Equal(50, Uw2.Game.Engine.Town.Navigators.Get(m, 90, 0x2B));
        gold = 10;
        Assert.Equal(-50, Uw2.Game.Engine.Town.Navigators.PayWages(m, 0, ref gold));
        Assert.Equal(0x80, Uw2.Game.Engine.Town.Navigators.Get(m, 90, 0x31) & 0x80);
    }

    [Fact]
    public void ANewShipNeedsAFreeMateWhoBecomesItsCaptain()
    {
        var m = new ScenarioMemory();
        for (int i = 0; i < Uw2.Game.Engine.Town.Navigators.MateSlots; i++)
            m.Write(new ScenarioMemory.Pointer(6, 3 + i), 0xFF, false);
        Assert.Equal(-1, Uw2.Game.Engine.Town.Navigators.FreeMate(m));
        Uw2.Game.Engine.Town.Navigators.Join(m, 90, 0, 30);
        Assert.Equal(90, Uw2.Game.Engine.Town.Navigators.FreeMate(m));
        Uw2.Game.Engine.Town.Navigators.Command(m, 90, aboard: true);
        Assert.Equal(-1, Uw2.Game.Engine.Town.Navigators.FreeMate(m));      // 처지 2 는 이미 배를 맡았다
        Uw2.Game.Engine.Town.Navigators.Command(m, 90, aboard: false);
        Assert.Equal(90, Uw2.Game.Engine.Town.Navigators.FreeMate(m));
    }

    [Fact]
    public void UsedShipsAreRolledOncePerPortAndCachedForThree()
    {
        var m = new ScenarioMemory();
        for (int i = 0; i < 24; i++) m.Write(new ScenarioMemory.Pointer(15, i), 0xFF, false);
        var types = Enumerable.Range(0, 25).Select(i => new StartData.ShipType(i, 0, 30, 70, 80, 0, 5, 50, 10, 4, 100)).ToList();
        int calls = 0;
        Uw2.Game.Engine.Town.Shipyard.EnterPort(m, 0, 0, 500, 100, types, n => { calls++; return 0; });
        // 리스본 갈래 0 의 줄 [0, 18, 5, …] 첫 칸 셋, 값싼 줄 첫 칸 둘 — 모두 0
        Assert.Equal([(0, 0), (1, 0), (2, 0), (3, 0), (4, 0)], Uw2.Game.Engine.Town.Shipyard.Used(m, 25).ToArray());
        Uw2.Game.Engine.Town.Shipyard.TakeUsed(m, 1);
        Uw2.Game.Engine.Town.Shipyard.EnterPort(m, 1, 0, 500, 100, types, _ => 1);
        Uw2.Game.Engine.Town.Shipyard.EnterPort(m, 0, 0, 500, 100, types, _ => throw new Exception("다시 굴리면 안 된다"));
        Assert.Equal(4, Uw2.Game.Engine.Town.Shipyard.Used(m, 25).Count);   // 산 칸은 빈 채로 남는다
        Assert.Equal(5, calls);
    }
}
