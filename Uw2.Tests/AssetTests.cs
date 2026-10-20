using Uw2.Support.Local.Formats;
using Xunit;

namespace Uw2.Tests;

/// <summary>저장소의 구운 자산(<c>asset/</c>)을 읽어 원본 표와 맞는지 본다.</summary>
public class AssetTests
{
    private static readonly AssetPack Pack = Load();

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
        Assert.Equal(2, Economy.CategoryOf(11));
        Assert.Equal(9, Economy.CategoryOf(45));
    }
}
