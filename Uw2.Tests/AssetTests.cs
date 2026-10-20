using Uw2.Support.Local.Formats;
using Xunit;

namespace Uw2.Tests;

/// <summary>저장소의 구운 자산(<c>asset/</c>)을 읽어 원본 표와 맞는지 본다.</summary>
public class AssetTests
{
    private static readonly AssetPack Pack = Load();

    private static string AssetDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, AssetPack.FolderName))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, AssetPack.FolderName);
        }
    }

    private static AssetPack Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, AssetPack.FolderName)))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return AssetPack.Load(Path.Combine(dir!.FullName, AssetPack.FolderName))
               ?? throw new InvalidDataException("asset/ 을 못 읽었습니다");
    }

    [Fact]
    public void StartTablesMatchTheOriginal()
    {
        var start = Pack.Start!;
        Assert.Equal(25, start.ShipTypes.Count);
        Assert.Equal(120, start.People.Count);
        Assert.Equal("조안", start.People[0].Name);
        Assert.Equal(75, start.People[0].Seamanship);
        Assert.Equal(70, start.Fleets.Count);
        // 주인공 함대는 배 없이, 남 함대는 바다에 나와 있다
        Assert.Equal(0, start.Fleets[0].ShipCount);
        Assert.True(start.Fleets[1].Active && start.Fleets[1].ShipCount > 0);
        Assert.Equal(33, start.Fleets[50].Port);          // 에르네스트는 암스테르담
        // 첫 배 — SNRn.DAT 첫 이벤트의 F9. 카탈리나만 갤리온이다
        Assert.Equal([5, 10, 5, 5, 5, 5], start.HeroShips);
        Assert.Equal("카라벨 라티나", start.Templates[5].Name);
        Assert.True(start.Templates[18].Oars);            // 경갤리는 노를 젓는다
        Assert.False(start.Templates[5].Oars);
        Assert.Equal(5000, start.HeroGold[3]);            // 에르네스트 · 피에트로 첫 돈 5000
        Assert.Equal(5000, start.HeroGold[4]);
    }

    [Fact]
    public void RouteGraphConnectsLisbonToIstanbul()
    {
        var g = Pack.Routes!;
        Assert.Equal(622, g.Nodes.Count);
        Assert.Equal((116, 349), (g.Nodes[0].X, g.Nodes[0].Y));
        var path = g.Path(g.Nearest(120, 358), g.Nearest(352, 344));
        Assert.True(path.Count > 2);
    }

    [Fact]
    public void DoorsAndWalkBounds()
    {
        Assert.NotNull(Pack.Doors);
        var lisbon = Pack.Doors!.Of(0);
        Assert.Equal((54, 67), lisbon[3]);                 // 항
        Assert.Equal((48, 7), lisbon[5]);                  // 왕궁
        Assert.Null(lisbon[11]);                           // 점술가는 없다
        Assert.Equal(new WalkBounds(29, 34, 40), Pack.PortWalkBounds[0]);
    }

    [Fact]
    public void FacesAreBaked() =>
        Assert.Equal(AssetPack.KaoCols * KaoImages.FaceW, Pack.FacesWidth);

    [Fact]
    public void LisbonMarketPricesFollow0x004208D0()
    {
        var eco = Pack.Economy!;
        Assert.Equal(46, eco.GoodsNames.Count);
        Assert.Equal("정향", eco.GoodsNames[0]);
        Assert.Equal(0, eco.States[0].Zone);                          // 리스본은 시장 0
        var sale = eco.ForSale(0);
        Assert.Equal(10, sale.Count);                                 // 아홉 + 특산품
        Assert.Equal((11, 36), sale[0]);                              // 유제품 30 x 102/100 x 12/10
        Assert.Equal((12, 24), sale[1]);                              // 어육
        Assert.Equal((15, 43), sale[3]);                              // 와인
        Assert.Equal((16, 45), sale[^1]);                             // 특산품 돌소금
        // 파는 값: 정향(향신료, 지수 49) 140 x 99/100 = 138, 특산품 돌소금은 38 x 102/200 = 19
        Assert.Equal(138, eco.SellPrice(0, 0));
        Assert.Equal(19, eco.SellPrice(0, 16));
        // 보급: 식량 102 x 20/100 = 20, 자재 (51+50) x 18/100 = 18, 탄약 102 x 120/100 = 122
        Assert.Equal((0, 20, 18, 122), (eco.SupplyPrice(0, 0), eco.SupplyPrice(0, 1), eco.SupplyPrice(0, 2), eco.SupplyPrice(0, 3)));
        Assert.Equal(2, Economy.CategoryOf(11));
        Assert.Equal(9, Economy.CategoryOf(45));
    }

    [Fact]
    public void BuildingMenusComeFromMenuDat()
    {
        var m = Pack.Menus!;
        Assert.Equal(["출  항", "보  급", "도  크"], m.ForBuilding(3));               // 항 — 목록 9
        Assert.Equal(4, m.ForBuilding(0).Count);                                       // 교역소 — 구입 · 매각 · 투자 · 시세·물가
        Assert.Equal("함선 건조", m.ForBuilding(2)[0]);                                // 조선소 — 목록 22
        Assert.Empty(m.ForBuilding(7));                                                // NPC 는 목록이 없다
    }

    [Fact]
    public void ShipyardsFollowTheIndustryRule()
    {
        var eco = Pack.Economy!;
        var types = Pack.Start!.ShipTypes;
        Assert.Equal(0, eco.States[0].Yard);                                  // 리스본
        var lisbon = eco.Buildable(0, types);
        Assert.Contains(5, lisbon);                                           // 카라벨 라티나
        Assert.All(lisbon, t => Assert.True(types[t].Field0 * 10 <= eco.States[0].Industry));
        Assert.Equal([24, 23, 22], Economy.ShipyardTypes[9]);                 // 나가사키 갈래
    }

    [Fact]
    public void JoaoMeetsHisFatherAtTheMansion()
    {
        string dir = Path.Combine(AssetDir, "snr");
        var vm = new Uw2.Game.Engine.ScenarioVm(File.ReadAllBytes(Path.Combine(dir, "SNR1.DAT")),
                                                ScenarioText.Load(Path.Combine(dir, "SNR1.MES")));
        // 리스본(0) · NPC(7) — 조안의 집. 처음에는 아버지가 탐험을 명하고 배를 맡긴다.
        var run = vm.Enter(0, 7, 1000, "조안", "페레로");
        Assert.NotNull(run);
        Assert.Contains(run!.Lines, l => l.Body.Contains("프레스터 존왕국"));
        Assert.Contains(5, run.Ships);                                        // 카라벨 라티나
        Assert.True(run.Stopped);                                             // F8 F2 — 집 볼일은 이것으로 끝
        Assert.Contains(run.Lines, l => l.Speaker == "페레로 공작");          // §$s 공작§
        Assert.Null(vm.Enter(5, 7, 1000, "조안", "페레로"));                  // 튀니스에는 걸린 것이 없다
    }
    [Fact]
    public void ChoiceGoesIntoAVariableAndFacesFollowTheWindow()
    {
        // 장 0 → 0x14, 절 0 → 0x16. 방아쇠 [0][7] → +8.  C0 02 · CC 0005 · C8 0000 · C9 01 0001 · E6 01(돈 += 변수 1) · FF
        var code = new byte[0x16 + 8 + 16];
        code[0x13] = 0x14; code[0x15] = 0x02;
        byte[] body = [0x00, 0x07, 0x00, 0x08, 0xFF, 0xFF, 0xFF, 0xFF,
                       0xC0, 0x02, 0xCC, 0x00, 0x05, 0xC8, 0x00, 0x00, 0xC9, 0x01, 0x00, 0x01, 0xE6, 0x01, 0xFF];
        code = [.. code[..0x16], .. body];
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cp = System.Text.Encoding.GetEncoding(949);
        byte[] t0 = cp.GetBytes("§아버지§/가거라"), t1 = cp.GetBytes("예\n아니오\n글쎄요");
        var mes = new List<byte>();
        foreach (int o in (int[])[12, 12 + t0.Length, 12 + t0.Length + t1.Length])
            mes.AddRange([(byte)(o >> 24), (byte)(o >> 16), (byte)(o >> 8), (byte)o]);
        mes.AddRange(t0); mes.AddRange(t1);

        var vm = new Uw2.Game.Engine.ScenarioVm(code, ScenarioText.FromBytes([.. mes]));
        IReadOnlyList<string>? asked = null;
        int before = -1;
        vm.Choose = (lines, options) => { asked = options; before = lines.Count; return 2; };
        var run = vm.Enter(0, 7, 100, "조안", "페레로")!;
        Assert.Equal(["예", "아니오", "글쎄요"], asked);
        Assert.Equal(1, before);                                   // 고르기 앞 대사를 먼저 넘긴다
        Assert.Empty(run.Pending);
        Assert.Equal(102, run.Gold);                               // 고른 차례 2 가 변수 1 에
        Assert.Equal((2, 5), (run.Lines[0].Window, run.Lines[0].Face));
    }

    /// <summary>방아쇠 [0][7] 하나에 <paramref name="body"/> 를 단 시나리오(장 0 → 0x14, 절 0 → 0x16).</summary>
    private static byte[] OneEvent(params byte[] body)
    {
        var head = new byte[0x16];
        head[0x13] = 0x14; head[0x15] = 0x02;
        return [.. head, 0x00, 0x07, 0x00, 0x08, 0xFF, 0xFF, 0xFF, 0xFF, .. body];
    }

    [Fact]
    public void PointersReachTheGoldAndArithmeticIsItsOwnClass()
    {
        var vm = new Uw2.Game.Engine.ScenarioVm(OneEvent(
            0xDC, 0x00, 0x02, 0x00, 0x00,      // 가리키개 0 = 표 2(돈) +0
            0x04, 0x05, 0x00,                  // 변수 5 = 가리키개 0 의 낱말
            0x4C, 0x05, 0x07,                  // 변수 5 += 7
            0xE4, 0x05,                        // 돈 = 변수 5
            0x5C, 0x00, 0x01,                  // 가리키개 0 자리 바이트 += 1
            0x8E, 0x05, 0xFF, 0x00, 0x1F,      // 변수 5 ≥ 255 가 아니면 +0x1F(끝)로
            0xE6, 0x05,                        // (건너뛴다)
            0xFF), ScenarioText.FromBytes(new byte[8]));
        var run = vm.Enter(0, 7, 100, "", "")!;
        Assert.Equal(108, run.Gold);
    }

    [Fact]
    public void NationTableGivesTheHeroesHomesAsCapitals()
    {
        var start = Load().Start!;
        // 리스본 · 세빌리아 · 2 · 29(옷토) · 8(피에트로) · 33(에르네스트), 일곱째는 4
        Assert.Equal([0, 1, 2, 29, 8, 33, 4], start.Capitals.ToArray());
    }

    [Fact]
    public void NavigatorsWaitInPubsAndJoinWhenFriendlyAndOutmatched()
    {
        var start = Load().Start!;
        Assert.Equal((0, 100, 100), (start.MiscBytes[0], start.MiscBytes[1], start.MiscBytes[2]));
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, start.PeopleBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, start.MiscBytes);
        Assert.Empty(Uw2.Game.Engine.Town.Navigators.Mates(m));
        // 새 게임 제노바(8) 주점에는 84 가 있다(+0x2F 0x91).
        var here = Uw2.Game.Engine.Town.Navigators.InTown(m, 8, hero: 4);
        Assert.Contains(84, here);
        Assert.Equal(Uw2.Game.Engine.Town.Navigators.Answer.NotFriendly,
                     Uw2.Game.Engine.Town.Navigators.Ask(m, 84, 4, 0, _ => 0, out _));
        while (Uw2.Game.Engine.Town.Navigators.Get(m, 84, Uw2.Game.Engine.Town.Navigators.Liking) <= 30)
            Uw2.Game.Engine.Town.Navigators.Treat(m, 84, 4);
        var answer = Uw2.Game.Engine.Town.Navigators.Ask(m, 84, 4, 0, _ => 0, out int wage);
        if (answer == Uw2.Game.Engine.Town.Navigators.Answer.Hired)
        {
            Assert.InRange(wage, 10, 200);
            Assert.True(Uw2.Game.Engine.Town.Navigators.Join(m, 84, 4, wage));
            Assert.Equal([(0, 84, wage)], Uw2.Game.Engine.Town.Navigators.Mates(m).ToArray());
            Assert.Equal(6, Uw2.Game.Engine.Town.Navigators.Get(m, 84, Uw2.Game.Engine.Town.Navigators.Status));
        }
        else Assert.Equal(Uw2.Game.Engine.Town.Navigators.Answer.Refused, answer);
    }

    [Fact]
    public void LisbonsNavigatorsWaitAtTheInnNotThePub()
    {
        var start = Load().Start!;
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, start.PeopleBytes);
        Assert.Empty(Uw2.Game.Engine.Town.Navigators.InTown(m, 0, hero: 0, pub: true));
        Assert.Equal([85, 96], Uw2.Game.Engine.Town.Navigators.InTown(m, 0, hero: 0, pub: false));
    }

    [Fact]
    public void ItemShopSellsFromThePortListAndRefusesASecondSword()
    {
        var pack = Load();
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.Town.ItemShop.Table, pack.Start!.ItemBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, pack.Start.MiscBytes);
        var dagger = Uw2.Game.Engine.Town.ItemShop.Get(m, 0);
        Assert.Equal(("단도", 500, 0), (dagger.Name, dagger.Price, dagger.Group));
        Assert.Empty(Uw2.Game.Engine.Town.ItemShop.Owned(m));
        var stock = Uw2.Game.Engine.Town.ItemShop.ForSale(pack.Economy!.StateBytes, 0, 24);
        Assert.InRange(stock.Count, 1, 3);
        int gold = 1000;
        Assert.Equal(Uw2.Game.Engine.Town.ItemShop.Result.Bought, Uw2.Game.Engine.Town.ItemShop.Buy(m, 0, ref gold));
        Assert.Equal(500, gold);
        Assert.Equal(Uw2.Game.Engine.Town.ItemShop.Result.Owned, Uw2.Game.Engine.Town.ItemShop.Buy(m, 0, ref gold));
        Assert.Equal(200, Uw2.Game.Engine.Town.ItemShop.Offer(dagger));            // 5 / 2 = 2 → 200
        Uw2.Game.Engine.Town.ItemShop.Sell(m, 0, 200, ref gold);
        Assert.Equal(700, gold);
        Assert.Empty(Uw2.Game.Engine.Town.ItemShop.Owned(m));
    }

    [Fact]
    public void PortNationByteIsTheLastOfTheRecord()
    {
        var ports = Load().Ports;
        // 서울 일곱이 나라 표 차례 0 ~ 6 으로 — 리스본 · 세빌리아 · 이스탄불 · 런던 · 제노바 · 암스테르담 · 알제
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], new[] { 0, 1, 2, 29, 8, 33, 4 }.Select(i => ports[i].Nation & 7).ToArray());
    }

    [Fact]
    public void DefectingMovesTheHeroToTheNewNationAndShiftsFavor()
    {
        var start = Load().Start!;
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, start.PeopleBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.HeroTable, start.HeroBytes);
        Uw2.Game.Engine.Town.Palace.Defect(m, 0, 3);                              // 조안이 잉글랜드로
        Assert.Equal(0x63, Uw2.Game.Engine.Town.Navigators.Get(m, 0, 0x31));       // 위 네 비트는 그대로
        int Favor(int n) => m.Read(Uw2.Game.Engine.ScenarioMemory.At(1, 0, 6 + n), false);
        Assert.Equal((70, 110), (Favor(0), Favor(3)));                             // 100 − 30 · 100 + 10
    }

    [Fact]
    public void YesNoRandomAndGoldInTenThousandsLandInVariables()
    {
        var vm = new Uw2.Game.Engine.ScenarioVm(OneEvent(
            0xE9, 0x03,                        // 예 · 아니오 → 깃발 3
            0xEA, 0x01,                        // 변수 1 = 돈 / 10000
            0xEB, 0x02, 0x00, 0x05,            // 변수 2 = 주사위(5)
            0xED, 0x04,                        // 변수 4 = 씨앗 >> 8
            0xAD, 0x03, 0x00, 0x18,            // 깃발 3 이 서 있지 않으면 끝(+0x18)으로
            0xE6, 0x01,                        // 돈 += 변수 1
            0xFF), ScenarioText.FromBytes(new byte[8]));
        vm.Choose = (_, options) => { Assert.Equal(["예", "아니오"], options); return 0; };
        var run = vm.Enter(0, 7, 35000, "", "")!;
        Assert.Equal(35003, run.Gold);                            // 예 → 깃발 3 → 돈 += 3
        var st = vm.Export();
        Assert.InRange(st.Vars[2], 0, 4);
    }

    [Fact]
    public void SharedScenarioRunsEverywhereWithoutRunningAway()
    {
        var pack = Load();
        string dir = Path.Combine(AssetDir, "snr");
        var code = File.ReadAllBytes(Path.Combine(dir, "SNR0.DAT"));
        var text = ScenarioText.Load(Path.Combine(dir, "SNR0.MES"));
        var lines = new List<string>();
        for (int port = 0; port < 100; port += 7)
            for (int b = 0; b < 12; b++)
            {
                var m = new Uw2.Game.Engine.ScenarioMemory();
                m.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, pack.Start!.PeopleBytes);
                m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, pack.Start.MiscBytes);
                m.Fill(Uw2.Game.Engine.ScenarioMemory.NationTable, pack.Start.NationBytes);
                m.Fill(Uw2.Game.Engine.ScenarioMemory.HeroTable, pack.Start.HeroBytes);
                var vm = new Uw2.Game.Engine.ScenarioVm(code, text, m);
                if (vm.Enter(port, b, 1000, "조안", "페레로") is { } run)
                {
                    Assert.True(run.Lines.Count < 200);
                    lines.Add($"{port,3} {b,2} {run.Lines.Count,3} {(run.Stopped ? "그침" : "끝")} " +
                              string.Join(" / ", run.Lines.Take(2).Select(l => l.Body.Replace('\n', ' '))));
                }
            }
        string? dump = Environment.GetEnvironmentVariable("UW2_SURVEY");
        if (dump != null) File.WriteAllLines(dump, lines);
    }

    [Fact]
    public void GuildOffersThreeJobsAndTakingOneOpensItsChapter()
    {
        var pack = Load();
        string dir = Path.Combine(AssetDir, "snr");
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, pack.Start!.PeopleBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, pack.Start.MiscBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.NationTable, pack.Start.NationBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.HeroTable, pack.Start.HeroBytes);
        m.FillPorts(pack.Ports.Select(p => (p.X, p.Y, p.Name, p.Nation)));
        var vm = new Uw2.Game.Engine.ScenarioVm(File.ReadAllBytes(Path.Combine(dir, "SNR0.DAT")),
                                                ScenarioText.Load(Path.Combine(dir, "SNR0.MES")), m);
        vm.GameValues[2] = 21; vm.GameValues[3] = 4; vm.GameValues[4] = 16; vm.GameValues[5] = 0; vm.GameValues[7] = 24;
        var log = new List<string>();
        var enter = vm.Enter(0, 6, 1000, "조안", "페레로");
        log.Add($"들어감: 대사 {enter?.Lines.Count} · 변수 0~8 {string.Join(",", Enumerable.Range(0, 9).Select(vm.GetVar))}");
        vm.SetVar(8, vm.GetVar(3));
        vm.Section = 1;
        vm.Choose = (_, o) => 0;
        var take = vm.Enter(0, 6, 1000, "조안", "페레로");
        log.Add($"맡음: 대사 {take?.Lines.Count} · 변수 6 = {vm.GetVar(6)} · 16 = {vm.GetVar(16)} · 돈 {take?.Gold}");
        if (take != null) log.AddRange(take.Lines.Select(l => l.Speaker + ": " + l.Body.Replace('\n', ' ')));
        Assert.NotNull(enter);
        string? dump = Environment.GetEnvironmentVariable("UW2_SURVEY");
        if (dump != null) File.WriteAllLines(dump, log);
    }

    [Fact]
    public void ATransportJobLoadsCargoAtThePickupPort()
    {
        var pack = Load();
        string dir = Path.Combine(AssetDir, "snr");
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, pack.Start!.PeopleBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, pack.Start.MiscBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.NationTable, pack.Start.NationBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.HeroTable, pack.Start.HeroBytes);
        m.FillPorts(pack.Ports.Select(p => (p.X, p.Y, p.Name, p.Nation)));
        m.FillGoodsNames(pack.Economy!.GoodsNames);
        var vm = new Uw2.Game.Engine.ScenarioVm(File.ReadAllBytes(Path.Combine(dir, "SNR0.DAT")),
                                                ScenarioText.Load(Path.Combine(dir, "SNR0.MES")), m) { EndsAtChapterZero = true };
        vm.GameValues[2] = 21; vm.GameValues[3] = 4; vm.GameValues[4] = 16; vm.GameValues[7] = 24;
        var log = new List<string>();
        var cargo = new List<(int, int)>();
        int held = 0;
        vm.Cargo = (g, a) => { cargo.Add((g, a)); if (a == int.MinValue) { int h = held; held = 0; return h; } held += a; return a; };
        vm.FreeRoom = 100;
        vm.Choose = (_, o) => 0;
        vm.Enter(0, 6, 1000, "조안", "페레로");
        vm.SetVar(8, vm.GetVar(3));
        vm.Section = 1;
        vm.Enter(0, 6, 1000, "조안", "페레로");
        vm.Chapter = vm.GetVar(6);
        vm.Section = 0;
        int from = vm.GetVar(16), to = vm.GetVar(17);
        log.Add($"장 {vm.Chapter} · 실을 곳 {from} · 내릴 곳 {to} · 변수 16~20 {string.Join(",", Enumerable.Range(16, 5).Select(vm.GetVar))}");
        foreach (int port in (int[])[from, to, from])
            for (int b = 0; b < 12; b++)
                if ((vm.GameValues[5] = port) >= 0 && vm.Enter(port, b, 1000, "조안", "페레로") is { Lines.Count: > 0 } run)
                    log.Add($"{port} {b}: 돈 {run.Gold} · 짐 [{string.Join(" ", cargo)}] · " +
                            string.Join(" / ", run.Lines.Select(l => l.Body.Replace('\n', ' '))));
        log.Add($"끝: 장 {vm.Chapter} · 절 {vm.Section}");
        string? dump = Environment.GetEnvironmentVariable("UW2_SURVEY");
        if (dump != null) File.WriteAllLines(dump, log);
        Assert.Equal(0, vm.Chapter);                                   // 다 날라 일이 끝났다
    }

    [Fact]
    public void EveryOfferedJobRunsWithoutRunningAway()
    {
        var pack = Load();
        string dir = Path.Combine(AssetDir, "snr");
        var log = new List<string>();
        for (int pick = 0; pick < 3; pick++)
        {
            var m = new Uw2.Game.Engine.ScenarioMemory();
            m.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, pack.Start!.PeopleBytes);
            m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, pack.Start.MiscBytes);
            m.Fill(Uw2.Game.Engine.ScenarioMemory.NationTable, pack.Start.NationBytes);
            m.Fill(Uw2.Game.Engine.ScenarioMemory.HeroTable, pack.Start.HeroBytes);
            m.Fill(Uw2.Game.Engine.ScenarioMemory.MarketTable, pack.Economy!.MarketBytes);
            m.Fill(Uw2.Game.Engine.ScenarioMemory.PortStateTable, pack.Economy.StateBytes);
            m.FillPorts(pack.Ports.Select(p => (p.X, p.Y, p.Name, p.Nation)));
            m.FillGoodsNames(pack.Economy.GoodsNames);
            var vm = new Uw2.Game.Engine.ScenarioVm(File.ReadAllBytes(Path.Combine(dir, "SNR0.DAT")),
                                                    ScenarioText.Load(Path.Combine(dir, "SNR0.MES")), m) { EndsAtChapterZero = true };
            vm.GameValues[2] = 21; vm.GameValues[3] = 4; vm.GameValues[4] = 16; vm.GameValues[7] = 24;
            int held = 0;
            vm.Cargo = (g, a) => { if (a == int.MinValue) { int h = held; held = 0; return h; } held += a; return a; };
            vm.FreeRoom = 100;
            vm.Choose = (_, o) => 0;
            vm.GameValues[5] = 0;
            vm.Enter(0, 6, 1000, "조안", "페레로");
            int kind = vm.GetVar(pick);
            vm.SetVar(8, vm.GetVar(3 + pick));
            vm.Section = pick + 1;
            var take = vm.Enter(0, 6, 5000, "조안", "페레로");
            vm.Chapter = vm.GetVar(6);
            vm.Section = 0;
            log.Add($"== 갈래 {kind} → 장 {vm.Chapter} · 변수 16 {vm.GetVar(16)} · 17 {vm.GetVar(17)} · " +
                    string.Join(" / ", take?.Lines.Select(l => l.Body.Replace('\n', ' ')) ?? []));
            int gold = 5000;
            foreach (int port in (int[])[vm.GetVar(16), vm.GetVar(17), vm.GetVar(16), 0])
                for (int b = 0; b < 12; b++)
                {
                    if (vm.Chapter == 0) break;
                    vm.GameValues[5] = port;
                    if (vm.Enter(port, b, gold, "조안", "페레로") is { Lines.Count: > 0 } run)
                    {
                        Assert.True(run.Lines.Count < 200);
                        gold = run.Gold;
                        log.Add($"  {port} {b}: 돈 {run.Gold} · 짐 {held} · 장 {vm.Chapter}.{vm.Section} · " +
                                string.Join(" / ", run.Lines.Select(l => l.Body.Replace('\n', ' '))));
                    }
                }
        }
        string? dump = Environment.GetEnvironmentVariable("UW2_SURVEY");
        if (dump != null) File.WriteAllLines(dump, log);
    }

    [Fact]
    public void JoaoMeetsHisFatherWithFullMemoryAndTheSharedScenarioFirst()
    {
        var pack = Load();
        string dir = Path.Combine(AssetDir, "snr");
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, pack.Start!.PeopleBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, pack.Start.MiscBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.NationTable, pack.Start.NationBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.HeroTable, pack.Start.HeroBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.FleetTable, pack.Start.FleetBytes);
        m.Fill(Uw2.Game.Engine.Town.ItemShop.Table, pack.Start.ItemBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.MarketTable, pack.Economy!.MarketBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.PortStateTable, pack.Economy.StateBytes);
        m.FillPorts(pack.Ports.Select(p => (p.X, p.Y, p.Name, p.Nation)));
        m.FillGoodsNames(pack.Economy.GoodsNames);
        var hero = new Uw2.Game.Engine.ScenarioVm(File.ReadAllBytes(Path.Combine(dir, "SNR1.DAT")),
                                                  ScenarioText.Load(Path.Combine(dir, "SNR1.MES")), m);
        var common = new Uw2.Game.Engine.ScenarioVm(File.ReadAllBytes(Path.Combine(dir, "SNR0.DAT")),
                                                    ScenarioText.Load(Path.Combine(dir, "SNR0.MES")), m) { EndsAtChapterZero = true };
        foreach (var vm in (Uw2.Game.Engine.ScenarioVm[])[hero, common])
        {
            vm.GameValues[2] = 21; vm.GameValues[3] = 4; vm.GameValues[4] = 16; vm.GameValues[5] = 0; vm.GameValues[7] = 24;
        }
        common.Enter(0, 7, 1000, "조안", "페레로");
        var run = hero.Enter(0, 7, 1000, "조안", "페레로");
        Assert.Contains(run!.Lines, l => l.Body.Contains("프레스터 존왕국"));
    }

    [Fact]
    public void GameMessagesReadAndFillLikePrintf()
    {
        var m = GameMessages.Load(Path.Combine(AssetDir, "snr", "MESSAGE.DAT"));
        Assert.Equal(1179, m.Count);
        Assert.Equal("선원은 충분해요.", m[0x1D]);
        Assert.Equal("문이 잠겨 있습니다.", m[0x2FD]);
        Assert.Equal("조안 페레로님이 아니십니까？", m.Format(1, "조안", "페레로"));
        Assert.Contains("경험이 120 더", m.Format(0x221, 120));
        Assert.Contains("금화 300이 드는데", m.Format(0xCD, 300));                  // %u(가) — 영 은 받침이 있다
        Assert.Contains("금화 25가 드는데", m.Format(0xCD, 25));
        Assert.Equal("그럼 금화 500닢을 입금시키겠습니다. 이율은 월 3%입니다.", m.Format(0x6A, 500));
    }

    [Fact]
    public void DiscoveriesSeedFiftyAndStonehengeIsFoundNearLondon()
    {
        var pack = Load();
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.Sea.Discoveries.Table, pack.Start!.DiscoveryBytes);
        var stone = Uw2.Game.Engine.Sea.Discoveries.Get(m, 0);
        Assert.Equal((168, 270, 10, 4), (stone.X, stone.Y, stone.Difficulty, stone.Kind));   // 유적
        var colony = ColonyText.Load(Path.Combine(AssetDir, "snr", "COLONY.DAT"));
        Assert.Equal(100, colony.Count);
        Assert.Equal("스톤 헨지", colony.Name(0));
        Assert.Equal(57, colony.Picture(0));                                        // KAO 발견물 판 57 이 거석
        Assert.StartsWith("중앙에 환상열석", colony.Text(0));

        var rng = new Random(1);
        Uw2.Game.Engine.Sea.Discoveries.Seed(m, rng.Next);
        int present = Enumerable.Range(0, Uw2.Game.Engine.Sea.Discoveries.Used)
            .Count(i => !Uw2.Game.Engine.Sea.Discoveries.Get(m, i).IsAbsent);
        Assert.Equal(Uw2.Game.Engine.Sea.Discoveries.InGame, present);

        // 스톤 헨지를 이번 게임에 두고 곁에서 찾는다 — 어려움 10 < 운 50 x 3/20 + 지식 20 x 7/20 = 14.
        m.Write(Uw2.Game.Engine.ScenarioMemory.At(16, 0, 6), stone.Flags & 0x7F, false);
        Assert.Equal(0, Uw2.Game.Engine.Sea.Discoveries.Near(m, 167, 268)?.Index);
        Assert.NotEqual(0, Uw2.Game.Engine.Sea.Discoveries.Near(m, 140, 268)?.Index ?? -1);
        Assert.Equal(Uw2.Game.Engine.Sea.Discoveries.Outcome.NoFood, Uw2.Game.Engine.Sea.Discoveries.Search(m, 0, 50, 20, 0, false));
        Assert.Equal(Uw2.Game.Engine.Sea.Discoveries.Outcome.Nothing, Uw2.Game.Engine.Sea.Discoveries.Search(m, 0, 20, 10, 0, true));
        Assert.Equal(Uw2.Game.Engine.Sea.Discoveries.Outcome.Found, Uw2.Game.Engine.Sea.Discoveries.Search(m, 0, 50, 20, 0, true));
        Assert.Equal(Uw2.Game.Engine.Sea.Discoveries.Outcome.AlreadyFound, Uw2.Game.Engine.Sea.Discoveries.Search(m, 0, 50, 20, 0, true));
        Assert.Equal(25, Uw2.Game.Engine.Sea.Discoveries.FightStrength(swordsmanship: 50, battleLevel: 25));   // v = 25 + 25 = 50
    }

    [Fact]
    public void NobleBuysAFoundDiscoveryForGoldAndAdventurerFame()
    {
        var pack = Load();
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.Town.Patrons.Table, pack.Start!.PatronBytes);
        m.Fill(Uw2.Game.Engine.Sea.Discoveries.Table, pack.Start.DiscoveryBytes);
        int count = Uw2.Game.Engine.Town.Patrons.InPort(m, 34);
        Assert.Equal(1, count);
        Assert.Equal("모리스 백작", Uw2.Game.Engine.Town.Patrons.Name(m, count));
        Assert.Equal(0, Uw2.Game.Engine.Town.Patrons.InPort(m, 0));                    // 리스본은 집사 마르코
        Assert.Empty(Uw2.Game.Engine.Town.Patrons.Reportable(m));                      // 0x80 — 이번 게임에 없다

        Uw2.Game.Engine.Town.Patrons.Contract(m, count);
        Assert.True(Uw2.Game.Engine.Town.Patrons.IsContracted(m, count));
        Assert.False(Uw2.Game.Engine.Town.Patrons.IsContracted(m, 0));

        m.Write(Uw2.Game.Engine.ScenarioMemory.At(16, 0, 6), 0x24, false);            // 스톤 헨지 — 찾았다
        Assert.Equal([0], Uw2.Game.Engine.Town.Patrons.Reportable(m));
        int reward = Uw2.Game.Engine.Town.Patrons.Report(m, count, 0, hero: 5, out int fame, out int message);
        Assert.Equal((2000, 80, 0x1E6), (reward, fame, message));                     // (5 − 격 1) x 10 x 50 · 10 x 8
        Assert.Equal(80, m.Read(Uw2.Game.Engine.ScenarioMemory.At(1, 5, 4), true));
        Assert.Empty(Uw2.Game.Engine.Town.Patrons.Reportable(m));
    }

    [Fact]
    public void RumourPointsAtTheFirstPresentDiscoveryInDegrees()
    {
        var pack = Load();
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.Sea.Discoveries.Table, pack.Start!.DiscoveryBytes);
        Assert.Null(Uw2.Game.Engine.Sea.Discoveries.Rumour(m, 100, _ => 0));        // 다 0x80 — 이번 게임에 없다
        m.Write(Uw2.Game.Engine.ScenarioMemory.At(16, 0, 6), 0x04, false);
        Assert.Equal((false, 0, true, 50), Uw2.Game.Engine.Sea.Discoveries.Rumour(m, 100, _ => 0));   // 스톤 헨지 서경 2 · 북위 51
        m.Write(Uw2.Game.Engine.ScenarioMemory.At(16, 2, 6), 0x04, false);
        m.Write(Uw2.Game.Engine.ScenarioMemory.At(16, 0, 6), 0x84, false);
        var pyramid = Uw2.Game.Engine.Sea.Discoveries.Rumour(m, 100, _ => 0)!.Value;
        Assert.True(pyramid.East && pyramid.North);                                    // 피라미드 — 동경 · 북위
        Assert.Equal((true, 180, false, 50), Uw2.Game.Engine.Sea.Discoveries.Rumour(m, 0, n => n == 80 ? 79 : 0));   // 엉터리 — 동경 |0 − 36| x 5
    }

    [Fact]
    public void LisbonBarmaidWarmsToBoastsAndGifts()
    {
        var pack = Load();
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.Town.Barmaids.Table, pack.Start!.BarmaidBytes);
        m.Fill(Uw2.Game.Engine.Sea.Discoveries.Table, pack.Start.DiscoveryBytes);
        m.Fill(Uw2.Game.Engine.Town.ItemShop.Table, pack.Start.ItemBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, pack.Start.MiscBytes);
        int maid = Uw2.Game.Engine.Town.Barmaids.InPort(m, 0);
        Assert.Equal("카를로타", Uw2.Game.Engine.Town.Barmaids.Name(m, maid));
        Assert.Equal(Uw2.Game.Engine.Town.Barmaids.Sincerity, Uw2.Game.Engine.Town.Barmaids.Taste(m, maid));
        Assert.Null(Uw2.Game.Engine.Town.Barmaids.AskTaste(m, maid, _ => 0));          // 호감 0 — 비밀이예요
        Assert.Null(Uw2.Game.Engine.Town.Barmaids.TellAge(m, maid));

        m.Write(Uw2.Game.Engine.ScenarioMemory.At(16, 0, 6), 0x24, false);            // 스톤 헨지(유적, 어려움 10)를 찾았다
        bool boasted = false;
        Assert.Equal(0x13E, Uw2.Game.Engine.Town.Barmaids.Boast(m, maid, 0, ref boasted));   // 성의 — 맞장구에 두 배
        Assert.Equal(4, Uw2.Game.Engine.Town.Barmaids.Get(m, maid, Uw2.Game.Engine.Town.Barmaids.Liking));
        Uw2.Game.Engine.Town.Barmaids.Boast(m, maid, 0, ref boasted);
        Assert.Equal(4, Uw2.Game.Engine.Town.Barmaids.Get(m, maid, Uw2.Game.Engine.Town.Barmaids.Liking));   // 한 번만

        int gold = 100000;
        Uw2.Game.Engine.Town.ItemShop.Buy(m, 51, ref gold);                             // 차이나드레스 — 힘 8
        Assert.Equal([51], Uw2.Game.Engine.Town.Barmaids.Giftable(m));
        Assert.Equal([0x141], Uw2.Game.Engine.Town.Barmaids.Gift(m, maid, 51));         // 성의라 맞장구는 없다
        Assert.Equal(12, Uw2.Game.Engine.Town.Barmaids.Get(m, maid, Uw2.Game.Engine.Town.Barmaids.Liking));
        Assert.Empty(Uw2.Game.Engine.Town.Barmaids.Giftable(m));
    }

    [Fact]
    public void JoaosAudienceIsAScenarioEventAtBuilding0x15()
    {
        string dir = Path.Combine(AssetDir, "snr");
        var vm = new Uw2.Game.Engine.ScenarioVm(File.ReadAllBytes(Path.Combine(dir, "SNR1.DAT")),
                                                ScenarioText.Load(Path.Combine(dir, "SNR1.MES")));
        vm.Chapter = 1;
        vm.Section = 1;
        var run = vm.Enter(0, 0x15, 1000, "조안", "페레로");                          // 리스본 왕궁 알현(0x00427800)
        Assert.NotNull(run);                                                          // 걸린 이벤트가 있다(조건은 기억에 따른다)
    }

    [Fact]
    public void ScenariosHangEventsOnDaysAtSea()
    {
        // 바다 이벤트는 자리 0xA0 · 건물 = 바다에 나온 날 수다(0x00418D45). 장 · 절을 돌며 걸린 것을 센다.
        string dir = Path.Combine(AssetDir, "snr");
        var found = new List<string>();
        for (int n = 0; n <= 6; n++)
        {
            var code = File.ReadAllBytes(Path.Combine(dir, $"SNR{n}.DAT"));
            var text = ScenarioText.Load(Path.Combine(dir, $"SNR{n}.MES"));
            for (int ch = 0; ch < 16; ch++)
                for (int sec = 0; sec < 16; sec++)
                    for (int day = 0; day < 40; day++)
                    {
                        var vm = new Uw2.Game.Engine.ScenarioVm(code, text) { Chapter = ch, Section = sec };
                        if (vm.Enter(0xA0, day, 1000, "a", "b") is { Lines.Count: > 0 }) { found.Add($"SNR{n} {ch}.{sec} day {day}"); break; }
                    }
        }
        Assert.Contains("SNR1 0.1 day 3", found);                                     // 조안 — 아버지를 뵌 뒤 바다 사흘째
        var joao = new Uw2.Game.Engine.ScenarioVm(File.ReadAllBytes(Path.Combine(dir, "SNR1.DAT")),
                                                  ScenarioText.Load(Path.Combine(dir, "SNR1.MES"))) { Chapter = 0, Section = 1 };
        Assert.False(joao.Enter(0xA0, 2, 1000, "조안", "페레로") is { Lines.Count: > 0 });     // 이틀째는 아니다
        var sea = joao.Enter(0xA0, 3, 1000, "조안", "페레로");
        Assert.Contains(sea!.Lines, l => l.Body.Contains("마냐나"));                // 밀항자에게 성을 지어 준다
    }

    [Fact]
    public void FcRaisesABarmaidsLikingUpToAHundred()
    {
        var vm = new Uw2.Game.Engine.ScenarioVm(OneEvent(0xFC, 0x01, 0x46, 0xFC, 0x01, 0x46, 0xFF), ScenarioText.FromBytes(new byte[8]));
        vm.Memory.Fill(Uw2.Game.Engine.Town.Barmaids.Table, Load().Start!.BarmaidBytes);
        vm.Enter(0, 7, 0, "", "");
        Assert.Equal(100, Uw2.Game.Engine.Town.Barmaids.Get(vm.Memory, 1, Uw2.Game.Engine.Town.Barmaids.Liking));   // 70 + 70 → 100
    }

    [Fact]
    public void E8ShowsAPersonsCard()
    {
        var vm = new Uw2.Game.Engine.ScenarioVm(OneEvent(0xE8, 0x00, 0xFF), ScenarioText.FromBytes(new byte[8]));
        vm.Memory.Fill(Uw2.Game.Engine.ScenarioMemory.PeopleTable, Load().Start!.PeopleBytes);
        var run = vm.Enter(0, 7, 0, "", "")!;
        Assert.Equal("조안 페레로", run.Lines[0].Speaker);
        Assert.StartsWith("항해 레벨", run.Lines[0].Body);
    }

    [Fact]
    public void FaNamesTheShipTheScenarioJustGave()
    {
        string dir = Path.Combine(AssetDir, "snr");
        // SNR1 의 FA 05 0084 를 떼어 돌린다 — 앞에 F9 05 로 배를 받는다.
        var vm = new Uw2.Game.Engine.ScenarioVm(OneEvent(0xF9, 0x05, 0x05, 0xFA, 0x05, 0x00, 0x84, 0xFF),
                                                ScenarioText.Load(Path.Combine(dir, "SNR1.MES")));
        var run = vm.Enter(0, 7, 1000, "조안", "페레로")!;
        Assert.Equal([5], run.Ships);
        Assert.Equal([(5, "헤르메스Ⅱ세")], run.ShipNames);
    }

    [Fact]
    public void EquipTogglesWithinAGroupAndToolsCannotBeWorn()
    {
        var pack = Load();
        var m = new Uw2.Game.Engine.ScenarioMemory();
        m.Fill(Uw2.Game.Engine.Town.ItemShop.Table, pack.Start!.ItemBytes);
        m.Fill(Uw2.Game.Engine.ScenarioMemory.MiscTable, pack.Start.MiscBytes);
        int gold = 1_000_000;
        foreach (int i in (int[])[0, 1, 20]) Uw2.Game.Engine.Town.ItemShop.Buy(m, i, ref gold);    // 단도 · 다음 칼 · 사분의
        Assert.Equal(Uw2.Game.Engine.Town.ItemShop.Get(m, 0).Group, Uw2.Game.Engine.Town.ItemShop.Get(m, 1).Group);
        Assert.True(Uw2.Game.Engine.Town.ItemShop.ToggleEquip(m, 0));
        Assert.True(Uw2.Game.Engine.Town.ItemShop.ToggleEquip(m, 1));
        Assert.False(Uw2.Game.Engine.Town.ItemShop.IsEquipped(m, 0));                       // 같은 갈래는 하나만
        Assert.False(Uw2.Game.Engine.Town.ItemShop.ToggleEquip(m, 1));
        Assert.Null(Uw2.Game.Engine.Town.ItemShop.ToggleEquip(m, 20));                       // 갈래 7 — 장비할 수 없다
    }
}
