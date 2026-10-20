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
}
