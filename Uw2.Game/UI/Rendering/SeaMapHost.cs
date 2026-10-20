using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Uw2.Game.Engine;
using Uw2.Game.Engine.Sea;
using Uw2.Game.Engine.Town;
using Uw2.Game.UI.Views;
using Uw2.Support.Local.Formats;
using Uw2.Support.Local.Helpers;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Uw2.Game.UI.Rendering;

/// <summary>
/// 자식 창 하나를 만들어 그 위에 DXGI 스왑체인을 걸고 세계지도를 그린다.
/// </summary>
/// <remarks>
/// 3편 <c>ShipMapHost</c> 와 같은 얼개다. <c>D3DImage</c> 를 안 쓰는 것은 공유 표면 복사를
/// 거치지 않으려는 것이다 — 대신 airspace 규칙대로 이 자식 창은 WPF 콘텐츠보다 늘 위에
/// 그려지니 그 위에 WPF 를 얹을 수는 없다.
/// </remarks>
public sealed class SeaMapHost : HwndHost
{
    // 창 클래스를 새로 등록하지 않고 미리 있는 STATIC 을 쓴다. 정적 컨트롤은 WM_NCHITTEST 에
    // HTTRANSPARENT 를 돌려주므로 마우스가 WPF 쪽으로 그대로 넘어간다 — 끌기·휠이 산다.
    private const string WndClass = "STATIC";
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;

    // CharSet 을 적어야 한다. 빠뜨리면 W 함수에 ANSI 문자열이 넘어가 클래스 이름을 못 찾는다.
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
                                                 int x, int y, int w, int h,
                                                 IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private readonly SeaMapRenderer _renderer = new();
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _backBufferView;
    private IntPtr _hwnd;
    private int _pixelW, _pixelH;
    private bool _ready, _dirty = true;

    private Point _dragFrom;
    private bool _dragging;

    /// <summary>화면 한 점이 나아가는 칸 수. 작을수록 확대다.</summary>
    /// <remarks>
    /// 뒤집으면 "칸당 화면 픽셀"이다 — 1/16 이면 칸당 열여섯 점이라 칩이 원본 크기다.
    /// 처음에는 온 지도가 보이게 잡는다.
    /// </remarks>
    public double CellsPerPixel { get; set; } = 2.0;

    /// <summary>화면 한가운데가 가리키는 칩 자리.</summary>
    public (double X, double Y) Center { get; set; } = (ChipMap.Width / 2.0, ChipMap.Height / 2.0);

    /// <summary>지도. 아직 안 올렸으면 null.</summary>
    public WorldMap? Map { get; private set; }

    /// <summary>칸을 칩으로 펴는 표. <c>DATA1.LZW</c> 를 못 읽으면 null.</summary>
    public ChipMap? Chips { get; private set; }

    /// <summary>바람 표. 없어도 지도는 돈다.</summary>
    public WindCurTable? Wind { get; private set; }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public string Status { get; private set; } = "";

    /// <summary>렌더러 — 칩·팔레트·모드를 밖에서 만진다.</summary>
    public SeaMapRenderer Renderer => _renderer;

    /// <summary>마우스가 가리키는 칸. 창 밖이면 마지막 값이다.</summary>
    public (int X, int Y) HoverCell { get; private set; }

    /// <summary>한 프레임 그린 뒤에 부른다 — 상태줄을 갱신하라는 뜻이다.</summary>
    public event Action? Painted;

    /// <summary>도시에서 건물 문에 섰을 때 — 건물 번호(<see cref="PortDoors.Names"/>)를 넘긴다.</summary>
    public event Action<int, bool>? BuildingEntered;

    /// <summary>함대를 새로 띄웠을 때 — 날씨 말 따위를 붙이라고 넘긴다.</summary>
    public event Action<Fleet>? FleetReady;

    /// <summary>
    /// 구워 둔 자산으로 연다. <b>게임이 없어도 된다</b> — 3편 <c>asset/</c> 과 같은 얼개다.
    /// </summary>
    public bool StartFromAssets(AssetPack pack)
    {
        Map = WorldMap.FromCells(pack.Cells);
        Chips = pack.ToChipMap();
        Wind = pack.Wind.Length > 0 ? WindCurTable.FromBytes(pack.Wind) : null;
        StartTables = pack.Start;
        if (pack.Faces.Length > 0) Faces = new FaceSheet(pack.Faces, pack.FacesWidth);
        if (AssetPack.FindFolder() is { } assetDir && Directory.Exists(Path.Combine(assetDir, "snr")))
            _scenarioDir = Path.Combine(assetDir, "snr");
        Routes = pack.Routes;
        _economyBase = pack.Economy;
        Economy = _economyBase?.Clone();
        Menus = pack.Menus;

        var cells = Chips.Expand(Map);
        if (!InitRenderer(cells, ChipMap.Width, ChipMap.Height)) return false;

        Sheet = ChipSheet.FromAtlas(pack.WorldAtlas, pack.WorldAtlasSize.W, pack.WorldAtlasSize.H,
                                    AssetPack.AtlasCols, ChipSheet.Packing.Packed4);
        _renderer.SetChips(Sheet);
        _renderer.UseChips = true;
        ChipNote = $"칩 {Sheet.Count}장 · 구운 자산";

        _portMaps = pack.PortMaps.Length > 0 ? PortMap.FromCells(pack.PortMaps) : null;
        _portChipNo = pack.PortChipSet.Length > 0 ? PortChipNumbers.FromBytes(pack.PortChipSet) : null;
        _portBounds = pack.PortWalkBounds.Length > 0 ? pack.PortWalkBounds : null;
        _doors = pack.Doors;
        _portSets = pack.PortAtlases.Length > 0
            ? [.. pack.PortAtlases.Select(a => ChipSheet.FromAtlas(
                  a, pack.PortAtlasSize.W, pack.PortAtlasSize.H,
                  AssetPack.AtlasCols, ChipSheet.Packing.Packed4))]
            : null;
        Ports = pack.Ports.Count > 0 ? PortTable.FromPorts(pack.Ports) : null;

        if (pack.Walker.Length > 0)
        {
            _walkArt = pack.Walker;
            _walkArtW = pack.WalkerSize.W;
            _walkArtH = pack.WalkerSize.H;
        }

        if (pack.Ship.Length == AssetPack.ShipW * AssetPack.ShipH)
        {
            _shipArt = pack.Ship;
            _shipW = AssetPack.ShipW;
            _shipH = AssetPack.ShipH;
        }

        Source = "구운 자산";
        return Finish();
    }

    /// <summary>게임 폴더에서 지도를 올린다. 못 열면 까닭을 <see cref="Status"/> 에 남기고 false.</summary>
    public bool Start(string gameDir)
    {
        try
        {
            Map = WorldMap.Load(Path.Combine(gameDir, "WORLDMAP.LZW"));
        }
        catch (Exception ex)
        {
            Status = $"WORLDMAP.LZW 를 읽지 못했습니다 — {ex.Message}";
            return false;
        }

        try { Wind = WindCurTable.Load(Path.Combine(gameDir, "WINDCUR.DAT")); }
        catch { Wind = null; }   // 없어도 지도는 돈다

        try { Routes = RouteGraph.FromData1(Path.Combine(gameDir, "DATA1.LZW")); } catch { Routes = null; }
        try { Menus = MenuTable.Load(Path.Combine(gameDir, "MENU.DAT")); } catch { Menus = null; }
        try { _economyBase = Economy.Load(Path.Combine(gameDir, "SDATA.DAT"), Path.Combine(gameDir, "KOUKAI2-han8.exe")); }
        catch { _economyBase = null; }
        Economy = _economyBase?.Clone();
        _scenarioDir = gameDir;
        try { StartTables = StartData.Load(Path.Combine(gameDir, "SDATA.DAT")); }
        catch { StartTables = null; }  // 없으면 배를 못 띄울 뿐 지도는 돈다

        byte[] chipCells;
        int cw, chh;
        try
        {
            Chips = ChipMap.FromData1(Path.Combine(gameDir, "DATA1.LZW"));
            chipCells = Chips.Expand(Map);
            cw = ChipMap.Width; chh = ChipMap.Height;
        }
        catch
        {
            // 그림표를 못 읽으면 칸 값을 그대로 깐다 — 모양은 보인다.
            Chips = null;
            chipCells = Map.Cells; cw = WorldMap.Width; chh = WorldMap.Height;
        }

        if (!InitRenderer(chipCells, cw, chh)) return false;

        // 칩 그림. 못 걸면 민색으로 물러선다 — 걸지도 못하고 칩 모드로 두면 화면이 까매진다.
        try
        {
            Sheet = WorldChips.Load(gameDir);
            _renderer.SetChips(Sheet);
            _renderer.UseChips = true;
            ChipNote = $"칩 {Sheet.Count}장 · {(Sheet.Kind == ChipSheet.Packing.Planar ? "겹판" : "4비트")}";
        }
        catch (Exception ex)
        {
            Sheet = null;
            _renderer.UseChips = false;
            ChipNote = $"칩을 못 읽어 민색으로 그립니다 — {ex.Message}";
        }

        // 항구. 없어도 세계지도는 돈다.
        try
        {
            PortTable.RegisterEncodings();
            _portMaps = PortMap.Load(Path.Combine(gameDir, "PORTMAP.LZW"));
            var portChips = PortChipSets.Load(Path.Combine(gameDir, "PORTCHIP.LZW"));
            _portSets = portChips.All;
            _portBounds = portChips.Bounds;
            _portChipNo = PortChipNumbers.Load(Path.Combine(gameDir, "CHIP_NO.DAT"));
            try { _doors = PortDoors.Load(Path.Combine(gameDir, "ZA_DAT.DAT")); } catch { _doors = null; }
            Ports = PortTable.Load(gameDir);
        }
        catch
        {
            _portMaps = null; _portSets = null; _portBounds = null; _portChipNo = null; Ports = null;
        }

        Build = GameFolder.BuildOf(gameDir);
        Source = gameDir;
        return Finish();
    }

    private bool InitRenderer(byte[] cells, int w, int h)
    {
        try
        {
            _renderer.Initialize(cells, w, h);
            return true;
        }
        catch (Exception ex)
        {
            // 셰이더가 안 붙으면 화면이 통째로 하얘질 뿐 까닭이 안 보인다. 파일에 남겨 둔다.
            Status = $"Direct3D 장치를 만들지 못했습니다 — {ex.Message}";
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "uw2-error.txt"), ex.ToString()); }
            catch { }
            return false;
        }
    }

    private bool Finish()
    {
        if (Wind != null) _renderer.SetFlow(FlowArrows.Grid(Wind, FlowMonth));
        if (Map != null && Chips != null)
        {
            Fleet = new Fleet(Map, Chips, Wind);
            Fleet.NewDay += () =>
            {
                Player.ConsumeDay();
                SyncLoad();
            };
            Fleet.NewMonth += () =>
            {
                Player.NewMonth();
                Economy?.NewMonth(_npcRng.Next);
                if (Scenario != null)
                {
                    int gold = Player.Gold;
                    Uw2.Game.Engine.Town.Navigators.PayWages(Scenario.Memory, HeroPersonNumber, ref gold);
                    Player.SetGold(gold);
                    Uw2.Game.Engine.Town.Navigators.Wander(Scenario.Memory, Roll);
                }
            };
            Crew(Fleet);
            FleetReady?.Invoke(Fleet);
        }
        if (_shipArt.Length > 0)
            _renderer.SetShipSprite(_shipArt, _shipW, _shipH, GamePalette.SeaScreen(),
                                    AssetPack.Transparent);
        _worldCells = _renderer.Cells;
        _worldW = _renderer.MapW;
        _worldH = _renderer.MapH;
        _worldSheet = Sheet;
        _ready = true;
        _dirty = true;
        CompositionTarget.Rendering += OnFrame;
        return true;
    }

    /// <summary>
    /// 세계지도에 바람·해류 화살표를 얹을지. 원본에 없는 보기다 — 항구에서는 안 그린다.
    /// </summary>
    public bool ShowArrows { get; set; }

    /// <summary>화살표가 보여 주는 바람의 달(0 부터). 게임 시작 달인 4(May)로 둔다.</summary>
    private const int FlowMonth = 4;

    /// <summary>
    /// 남의 함대 — 새 게임의 일흔 함대 가운데 바다에 나와 있고(+0x29 비트 0) 배가 있는 것.
    /// 아직 움직이지 않는다(뱃길 그래프 DATA1.LZW 4번 청크를 옮기면 간다).
    /// </summary>
    public List<NpcFleet> Others { get; } = [];

    /// <summary>고르기 목록(MENU.DAT). 없으면 건물에 들어도 고를 것이 없다.</summary>
    public MenuTable? Menus { get; private set; }

    /// <summary>교역 표. 없으면 교역소가 비어 있다.</summary>
    public Economy? Economy { get; private set; }

    /// <summary>읽어 온 그대로의 교역 표 — 새 게임마다 사본을 뜬다(달마다 항구 형편이 바뀌므로).</summary>
    private Economy? _economyBase;

    /// <summary>뱃길 그래프(DATA1.LZW 4번 청크). 없으면 남 함대가 안 움직인다.</summary>
    public RouteGraph? Routes { get; private set; }

    private double _othersScale = -1;
    private bool _othersMoved = true;
    private readonly Random _npcRng = new();

    /// <summary>남의 함대 자리를 렌더러에 올린다. 눈금이 바뀌었거나 움직였을 때만 다시 짓는다.</summary>
    private void PushOthers()
    {
        double scale = Math.Clamp(1.0 / CellsPerPixel / 2.0, 0.35, 2.5) * ShipScale;
        if (Math.Abs(scale - _othersScale) < 1e-9 && !_othersMoved) return;
        _othersScale = scale;
        _othersMoved = false;
        // 제 배와 같은 자리 잡기 — 함대 자리가 그림 가로 한가운데 · 밑변이다. 항구에 든 함대는 안 그린다.
        var list = Others.Where(f => !f.Docked).Select(f => ((float)f.X, (float)f.Y, f.Heading is > 4)).ToList();
        _renderer.SetOthers(list, (float)(_shipW * scale), (float)(_shipH * scale));
    }

    /// <summary>선종 · 사람 표(<c>SDATA.DAT</c>). 없으면 배를 띄우지 못한다.</summary>
    public StartData? StartTables { get; private set; }

    /// <summary>
    /// 고른 주인공 — 첫 창의 차례(0 조안 · 1 카탈리나 · 2 피에트로 · 3 알리 · 4 에른스트 · 5 오토)다.
    /// 사람 표의 차례는 다르다(<see cref="HeroPerson"/>).
    /// </summary>
    public int Hero { get; private set; }

    /// <summary>첫 창의 주인공 차례 → 사람 표 번호. 표는 조안 · 카탈리나 · 옷토 · 에르네스트 · 피에트로 · 알 순이다.</summary>
    private static readonly int[] HeroPerson = [0, 1, 4, 5, 3, 2];

    /// <summary>
    /// 첫 배를 못 읽었을 때 띄우는 선종 — 카라벨 라티나. 주인공 다섯이 이것으로 시작한다(<see cref="Scenario"/>).
    /// </summary>
    public const int StandInShipType = 5;

    /// <summary>제 함대 배의 이름 — 본보기(선종 이름)를 그대로 쓴다. 명명은 아직이다.</summary>
    public string ShipName { get; private set; } = "";

    /// <summary>시나리오 파일이 있는 폴더 — 구운 자산이면 <c>asset/snr</c>, 게임 폴더면 그 폴더.</summary>
    private string? _scenarioDir;

    /// <summary>얼굴 판 — 시나리오 대사 창이 쓴다. 구워 두지 않았으면 null.</summary>
    public FaceSheet? Faces { get; private set; }

    /// <summary>주인공의 시나리오 기계. 파일이 없으면 null.</summary>
    public ScenarioVm? Scenario { get; private set; }

    /// <summary>주인공 이름 · 성 — 대사의 $n · $s.</summary>
    public (string Name, string Family) HeroName { get; private set; } = ("", "");

    private void LoadScenario(int person)
    {
        Scenario = null;
        if (_scenarioDir == null) return;
        for (int n = 1; n <= 6; n++)
        {
            string dat = Path.Combine(_scenarioDir, $"SNR{n}.DAT"), mes = Path.Combine(_scenarioDir, $"SNR{n}.MES");
            if (!File.Exists(dat) || !File.Exists(mes)) continue;
            var code = File.ReadAllBytes(dat);
            if (Uw2.Support.Local.Formats.Scenario.OwnerOf(code) != person) continue;
            Scenario = new ScenarioVm(code, ScenarioText.Load(mes));
            if (StartTables is { FleetBytes.Length: > 0 } st) Scenario.Memory.Fill(ScenarioMemory.FleetTable, st.FleetBytes);
            if (StartTables is { PeopleBytes.Length: > 0 } sp) Scenario.Memory.Fill(ScenarioMemory.PeopleTable, sp.PeopleBytes);
            if (StartTables is { NationBytes.Length: > 0 } sn) Scenario.Memory.Fill(ScenarioMemory.NationTable, sn.NationBytes);
            if (StartTables is { HeroBytes.Length: > 0 } sh) Scenario.Memory.Fill(ScenarioMemory.HeroTable, sh.HeroBytes);
            // 중고선 칸(표 15)은 비워 둔다 — 0 이면 「리스본의 발사 다섯」으로 읽힌다.
            for (int i = 0; i < 8 * Uw2.Game.Engine.Town.Shipyard.UsedRecords; i++)
                Scenario.Memory.Write(new ScenarioMemory.Pointer(Uw2.Game.Engine.Town.Shipyard.UsedTable, i), 0xFF, false);
            if (StartTables is { ItemBytes.Length: > 0 } si) Scenario.Memory.Fill(Uw2.Game.Engine.Town.ItemShop.Table, si.ItemBytes);
            if (StartTables is { MiscBytes.Length: > 0 } sm)
            {
                Scenario.Memory.Fill(ScenarioMemory.MiscTable, sm.MiscBytes);
            }
            else
                for (int i = 0; i < Navigators.MateSlots; i++)
                    Scenario.Memory.Write(new ScenarioMemory.Pointer(ScenarioMemory.MiscTable, Navigators.MateFirst + i), 0xFF, false);
            Scenario.Memory.Write(new ScenarioMemory.Pointer(ScenarioMemory.GoldTable, ScenarioMemory.HeroField), person, false);
            if (Economy is { } eco)
            {
                Scenario.Memory.Fill(ScenarioMemory.MarketTable, eco.MarketBytes);
                Scenario.Memory.Fill(ScenarioMemory.PortStateTable, eco.StateBytes);
            }
            return;
        }
    }

    /// <summary>시나리오가 돌린 결과를 들인다 — 돈, 그리고 아직 없는 선종이면 배.</summary>
    public void Apply(ScenarioVm.Run run)
    {
        Player.SetGold(run.Gold);
        if (Scenario != null) ReadShipSide(Scenario.Memory);
        if (StartTables is not { } start || Fleet == null) return;
        foreach (int type in run.Ships)
            if (!Player.Ships.Any(s => s.Type == type)) AddShip(type);
    }

    /// <summary>
    /// 제 배 곁 30바이트(표 9)를 시나리오 기억에 쓴다 — 보급 넷(+0) · 짐 양 다섯(+0x0C) · 짐 번호 다섯(+0x16, 0xFF 빈 칸).
    /// 게임 값(날짜 · 시각 · 머문 항구 · 바다 칸)도 함께 채운다. 건물에 들기 전에 부른다.
    /// </summary>
    public void WriteShipSide()
    {
        if (Scenario == null) return;
        if (Fleet != null)
        {
            var g = Scenario.GameValues;
            g[ScenarioVm.ValueArea] = (int)Fleet.Y / 24 * 90 + (int)Fleet.X / 24;
            g[ScenarioVm.ValueYear] = Fleet.Date.Year;
            g[ScenarioVm.ValueMonth] = Fleet.Date.Month;
            g[ScenarioVm.ValueDay] = Fleet.Date.Day;
            g[ScenarioVm.ValueTime] = Fleet.TimeOfDay;
        }
        Scenario.GameValues[ScenarioVm.ValuePort] = CurrentPort < 0 ? 255 : CurrentPort;
        if (Economy != null) Scenario.Memory.Fill(ScenarioMemory.PortStateTable, Economy.StateBytes);
        // 은행 잔고 — 표 2 +4 낱말 x 100 + +6 바이트(0x0049F5AC · 0x0049F5AE).
        int hundreds = Player.Balance / 100, rest = Player.Balance - hundreds * 100;
        if (rest < 0) { hundreds--; rest += 100; }
        Scenario.Memory.Write(new ScenarioMemory.Pointer(ScenarioMemory.GoldTable, 4), hundreds & 0xFFFF, true);
        Scenario.Memory.Write(new ScenarioMemory.Pointer(ScenarioMemory.GoldTable, 6), rest, false);
        var m = Scenario.Memory;
        for (int i = 0; i < Player.Ships.Count; i++)
        {
            var ship = Player.Ships[i];
            for (int k = 0; k < PlayerState.SupplyKinds; k++)
                m.Write(ScenarioMemory.At(ScenarioMemory.PlayerShipTable, i, k * 2), ship.Supplies[k], true);
            for (int k = 0; k < PlayerState.CargoSlots; k++)
            {
                m.Write(ScenarioMemory.At(ScenarioMemory.PlayerShipTable, i, 0x0C + k * 2), ship.Cargo[k].Amount, true);
                m.Write(ScenarioMemory.At(ScenarioMemory.PlayerShipTable, i, 0x16 + k), ship.Cargo[k].Good < 0 ? 0xFF : ship.Cargo[k].Good, false);
            }
        }
    }

    private void ReadShipSide(ScenarioMemory m)
    {
        for (int i = 0; i < Player.Ships.Count; i++)
        {
            var ship = Player.Ships[i];
            for (int k = 0; k < PlayerState.SupplyKinds; k++)
                ship.Supplies[k] = m.Read(ScenarioMemory.At(ScenarioMemory.PlayerShipTable, i, k * 2), true);
            for (int k = 0; k < PlayerState.CargoSlots; k++)
            {
                int good = m.Read(ScenarioMemory.At(ScenarioMemory.PlayerShipTable, i, 0x16 + k), false);
                int amount = m.Read(ScenarioMemory.At(ScenarioMemory.PlayerShipTable, i, 0x0C + k * 2), true);
                ship.Cargo[k] = good == 0xFF || amount == 0 ? (-1, 0) : (good, amount);
            }
        }
        SyncLoad();
    }

    /// <summary>지금 게임을 세이브 한 칸으로 담는다.</summary>
    public SaveGame Capture(int homePort)
    {
        var save = new SaveGame
        {
            Hero = Hero, Port = CurrentPort, HomePort = homePort, SavedAt = DateTime.Now,
            Fleet = Fleet?.Export() ?? new(), Player = SaveGame.From(Player), Scenario = Scenario?.Export(),
            PortStates = Economy is { } eco ? (byte[])eco.StateBytes.Clone() : [],
        };
        string where = CurrentPort >= 0 ? SceneName : "바다";
        save.Summary = $"{HeroName.Name} {HeroName.Family} · {Fleet?.Date} · {where} · 돈 {Player.Gold}";
        return save;
    }

    /// <summary>
    /// 세이브를 들인다 — 주인공을 새로 세운 뒤(<see cref="SetHero"/>) 돈 · 배 · 시나리오 · 항구 형편 · 함대 자리를 덮어쓴다.
    /// 항구에 있었으면 그 항구를, 아니면 그 바다를 띄운다.
    /// </summary>
    public void Restore(SaveGame save)
    {
        SetHero(save.Hero);
        if (Fleet == null || StartTables is not { } start) return;
        Player = SaveGame.ToPlayer(save.Player);
        Fleet.Ships.Clear();
        foreach (var s in Player.Ships)
        {
            var t = start.Templates.Count > s.Type ? start.Templates[s.Type] : default;
            var skipper = s.Captain >= 0 && s.Captain < start.People.Count ? start.People[s.Captain] : start.People[HeroPerson[Hero]];
            Fleet.Ships.Add(ShipSailing.Fresh(start.ShipTypes[s.Type], skipper, t.Oars));
        }
        ShipName = Player.Ships.Count > 0 ? Player.Ships[0].Name : "";
        SyncLoad();
        if (save.Scenario != null) Scenario?.Import(save.Scenario);
        if (save.PortStates.Length > 0) Economy?.RestoreStates(save.PortStates);
        Fleet.Import(save.Fleet);
        if (save.Port >= 0) ShowPort(save.Port);
        else
        {
            ShowWorld();
            Fleet.UnderSail = true;
            AtSea = true;
            CenterOnShip();
            _dirty = true;
        }
    }

    /// <summary>함대 한 벌의 배 칸 — 열(<see cref="StartData.SlotCount"/>).</summary>
    public const int MaxShips = StartData.SlotCount;

    /// <summary>그 선종의 새 배를 함대 끝에 넣는다. 칸이 다 찼거나 선종이 없으면 false.</summary>
    public bool AddShip(int type, int grade = -1, int captain = -1)
    {
        if (StartTables is not { } start || Fleet == null) return false;
        if (type < 0 || type >= start.ShipTypes.Count || Player.Ships.Count >= MaxShips) return false;
        var t = start.Templates.Count > type ? start.Templates[type] : default;
        // 새 배는 선원 없이 온다(0x0043DF6A) — 주점에서 고용한다. 빠르기 셈의 항해 레벨 · 항해술은 그 배 선장 것이다.
        var skipper = captain >= 0 && captain < start.People.Count ? start.People[captain] : start.People[HeroPerson[Hero]];
        Fleet.Ships.Add(ShipSailing.Fresh(start.ShipTypes[type], skipper, t.Oars) with { Crew = 0 });
        var ship = NewShip(start, type, crew: 0);
        ship.Captain = captain;
        Player.Ships.Add(grade < 0 ? ship : new PlayerState.Ship
        {
            Type = ship.Type, Name = ship.Name, Room = ship.Room, MaxCrew = ship.MaxCrew, Crew = 0,
            Hull = ship.Hull, MaxHull = ship.MaxHull, Upwind = ship.Upwind, Sail = ship.Sail, Grade = grade, Captain = captain,
        });
        if (captain >= 0 && Scenario != null) Uw2.Game.Engine.Town.Navigators.Command(Scenario.Memory, captain, aboard: true);
        SyncLoad();
        return true;
    }

    /// <summary>새 배를 맡을 동료 — 시나리오 기억이 없으면(게임 폴더 없이) -1 이고 막지 않는다.</summary>
    public int FreeMate => Scenario is { } vm ? Uw2.Game.Engine.Town.Navigators.FreeMate(vm.Memory) : -1;

    /// <summary>동료 제도를 쓰는지 — 시나리오 기억이 있을 때만.</summary>
    public bool HasMates => Scenario != null;

    /// <summary>새 배 — 내구는 선종의 9/10, 선원 자리 · 짐 자리 · 등급은 본보기에서.</summary>
    private static PlayerState.Ship NewShip(StartData start, int type, int crew)
    {
        var st = start.ShipTypes[type];
        var t = start.Templates.Count > type ? start.Templates[type] : default;
        int hull = Uw2.Game.Engine.Town.Shipyard.NewHull(st);
        return new PlayerState.Ship
        {
            Type = type,
            Name = t.Name ?? "",
            Room = t.Name != null ? t.CargoRoom : st.Capacity,
            MaxCrew = Math.Max(t.Name != null ? t.CrewRoom : st.MaxCrew, crew),
            Crew = crew,
            Hull = hull, MaxHull = hull, Upwind = st.Upwind, Sail = st.Sail,
            Grade = t.Flags & 7,
        };
    }

    /// <summary>배 한 척을 함대에서 뺀다 — 매각. 마지막 한 척은 못 뺀다.</summary>
    public bool RemoveShip(int index)
    {
        if (Fleet == null || Player.Ships.Count <= 1 || index < 0 || index >= Player.Ships.Count) return false;
        if (Player.Ships[index].Captain is >= 0 and var c && Scenario != null)
            Uw2.Game.Engine.Town.Navigators.Command(Scenario.Memory, c, aboard: false);
        Player.Ships.RemoveAt(index);
        if (index < Fleet.Ships.Count) Fleet.Ships.RemoveAt(index);
        ShipName = Player.Ships[0].Name;
        SyncLoad();
        return true;
    }

    /// <summary>돈과 짐. 주인공을 정하면 새로 만든다.</summary>
    public PlayerState Player { get; private set; } = new(0);

    /// <summary>짐 · 선원이 바뀌면 부른다 — 배의 빠르기 셈에 짐 몫(180 - 짐%)과 선원 몫을 넣는다.</summary>
    public void SyncLoad()
    {
        if (Fleet == null) return;
        for (int i = 0; i < Fleet.Ships.Count && i < Player.Ships.Count; i++)
            Fleet.Ships[i] = Fleet.Ships[i] with { Load = Player.Ships[i].Load, Crew = Player.Ships[i].Crew };
    }

    /// <summary>주인공의 사람 번호.</summary>
    public int HeroPersonNumber => HeroPerson[Hero];

    /// <summary>게임 주사위 — 0 이상 n 미만(<c>0x0044E0D2</c> 자리).</summary>
    public int Roll(int n) => n < 2 ? 0 : _npcRng.Next(n);

    /// <summary>사람 이름 — 「이름 성」.</summary>
    public string PersonName(int person) =>
        StartTables is { } st && person >= 0 && person < st.People.Count ? $"{st.People[person].Name} {st.People[person].Family}".Trim() : $"#{person}";

    /// <summary>주인공 작위 — 시나리오 기억 표 1 +0x0D. 새 게임은 0 이고 시나리오가 올린다.</summary>
    public int HeroRank =>
        Scenario?.Memory.Read(ScenarioMemory.At(ScenarioMemory.HeroTable, HeroPerson[Hero], ScenarioMemory.RankField), false) ?? 0;

    /// <summary>주인공의 나라 — 사람 표 +0x31 의 아래 네 비트(<c>0x0042A5DB</c>). 6 은 나라 없음.</summary>
    public int HeroNation
    {
        get
        {
            int o = HeroPerson[Hero] * StartData.PersonSize + 0x31;
            return StartTables is { } st && o < st.PeopleBytes.Length ? Math.Min(Economy.NoNation, st.PeopleBytes[o] & 0x0F) : Economy.NoNation;
        }
    }

    /// <summary>주인공 사람 기록 — 주점 셈(매력)이 쓴다.</summary>
    public StartData.Person? HeroRecord =>
        StartTables is { } start && HeroPerson[Hero] < start.People.Count ? start.People[HeroPerson[Hero]] : null;

    /// <summary>주인공을 정하고 그 사람을 선장으로 배를 다시 태운다.</summary>
    public void SetHero(int hero)
    {
        Hero = Math.Clamp(hero, 0, HeroPerson.Length - 1);
        if (Fleet != null) Crew(Fleet);
    }

    private void Crew(Fleet fleet)
    {
        fleet.Ships.Clear();
        if (StartTables is not { } start) return;
        Others.Clear();
        // 뭍 위 자리(항구에 든 함대)는 빼고 바다에 나온 함대만 띄운다. 고향은 +0x1C, 갈 데는 +0x04 · +0x06 이다.
        if (Routes is { } routes && Ports is { } ports)
            foreach (var f in start.Fleets.Where(f => f.Active && f.ShipCount > 0 && fleet.CanSail(f.X, f.Y)))
            {
                var home = f.Home < ports.Ports.Count ? (ports.Ports[f.Home].X, ports.Ports[f.Home].Y) : (f.TargetX, f.TargetY);
                Others.Add(new NpcFleet(fleet, routes, _npcRng, f, start, home, (f.TargetX, f.TargetY)));
            }
        _othersScale = -1;
        // 첫 배는 그 주인공 시나리오(SNRn.DAT)의 첫 이벤트가 주는 선종이다 — 배 주기 명령 F9.
        Economy = _economyBase?.Clone();
        int person = HeroPerson[Hero];
        var captain = start.People[person];
        HeroName = (captain.Name, captain.Family);
        LoadScenario(person);
        int type = person < start.HeroShips.Count && start.HeroShips[person] is >= 0 and var t ? t : StandInShipType;
        bool oars = type < start.Templates.Count && start.Templates[type].Oars;
        ShipName = type < start.Templates.Count ? start.Templates[type].Name : "";
        var first = ShipSailing.Fresh(start.ShipTypes[type], captain, oars);
        fleet.Ships.Add(first);
        int gold = person < start.HeroGold.Count ? start.HeroGold[person] : 0;
        Player = new PlayerState(gold);
        Player.Ships.Add(NewShip(start, type, first.Crew));
        if (start.MiscBytes.Length > 2)
        {
            Player.WaterRate = start.MiscBytes[1];
            Player.FoodRate = start.MiscBytes[2];
        }
    }

    /// <summary>어디서 읽었는지 — 게임 폴더 아니면 「구운 자산」.</summary>
    public string Source { get; private set; } = "";

    /// <summary>바다 위의 함대. 지도를 못 올렸으면 null.</summary>
    public Fleet? Fleet { get; private set; }

    /// <summary>도시 안을 걷는 사람. 항구를 보고 있을 때만 있다.</summary>
    public Walker? Walker { get; private set; }

    /// <summary>이번 틱에 누른 방향. 창이 넣어 준다.</summary>
    public (int X, int Y) WalkInput { get; set; }

    private byte[] _walkArt = [];
    private int _walkArtW, _walkArtH;

    /// <summary>배를 띄우고 방향키로 몰지. 끄면 방향키가 지도를 옮긴다.</summary>
    public bool Sailing { get; set; } = true;

    /// <summary>출항했는지. 항구에 들면 내리고 출항하면 올린다 — 매여 있는 동안은 세계지도를 봐도 배가 안 간다.</summary>
    public bool AtSea { get; private set; }

    /// <summary>배가 화면 가장자리 이만큼 안에 들면 화면을 넘긴다(실픽셀).</summary>
    private const int FollowMargin = 120;

    /// <summary>배 그림 한 변을 몇 배로 늘려 그릴지.</summary>
    public double ShipScale { get; set; } = 1;

    private byte[] _shipArt = [];
    private int _shipW, _shipH;
    private TimeSpan _lastTick;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _carry;

    /// <summary>배를 그 항구 앞바다에 띄운다.</summary>
    public bool SailFrom(int port)
    {
        if (Fleet == null || Ports == null || port < 0 || port >= Ports.Ports.Count) return false;
        var p = Ports.Ports[port];
        if (!Fleet.PlaceNearSea(p.X, p.Y)) return false;

        Fleet.Heading = 0;
        Fleet.UnderSail = true;
        AtSea = true;
        CenterOnShip();
        _dirty = true;
        return true;
    }

    /// <summary>
    /// 그림 자리를 배와 사람 사이로 옮긴다. 렌더러에는 그림 자리가 하나뿐이라 갈아 끼운다.
    /// </summary>
    private void UseWalkerSprite(bool walker)
    {
        if (walker && _walkArt.Length > 0)
            _renderer.SetShipSprite(_walkArt, _walkArtW, _walkArtH, GamePalette.SeaScreen(),
                                    AssetPack.CharClear);
        else if (!walker && _shipArt.Length > 0)
            _renderer.SetShipSprite(_shipArt, _shipW, _shipH, GamePalette.SeaScreen(),
                                    AssetPack.Transparent);
        _renderer.ShipFacesWest = false;
    }

    private void CenterOnShip()
    {
        if (Fleet != null) Center = (Fleet.X, Fleet.Y);
    }

    /// <summary>세계 칩 벌. 못 읽었으면 null 이고 그때는 민색으로 그린다.</summary>
    public ChipSheet? Sheet { get; private set; }

    /// <summary>칩을 어떻게 읽었는지 한 줄로. 상태줄에 낸다.</summary>
    public string ChipNote { get; private set; } = "";

    /// <summary>어느 판의 폴더인지.</summary>
    public GameFolder.Build Build { get; private set; } = GameFolder.Build.Unknown;

    /// <summary>항구 표. 못 읽으면 null 이고 그때는 번호로만 고른다.</summary>
    public PortTable? Ports { get; private set; }

    /// <summary>지금 무엇을 보고 있나. 음수면 세계지도다.</summary>
    public int CurrentPort { get; private set; } = -1;

    /// <summary>세계지도로 돌아간다.</summary>
    public void ShowWorld()
    {
        if (Map == null || _worldCells == null) return;
        CurrentPort = -1;
        Walker = null;
        UseWalkerSprite(false);
        _renderer.SetCells(_worldCells, _worldW, _worldH);
        _renderer.WrapX = true;
        _renderer.GridStep = WorldMap.Tile * ChipMap.PerCell;
        if (_worldSheet != null) _renderer.SetChips(_worldSheet);
        FitToWindow();
        _dirty = true;
    }

    /// <summary>항구 하나를 띄운다. 칩 벌은 <c>CHIP_NO.DAT</c> 이 가리키는 것을 쓴다.</summary>
    public bool ShowPort(int port)
    {
        if (_portMaps == null || _portSets == null || _portChipNo == null) return false;
        if (_portSets.Length == 0) return false;
        if (port < 0 || port >= _portMaps.Ports) return false;

        CurrentPort = port;
        if (Scenario != null && Economy is { } eco && StartTables is { } st && port < eco.States.Count)
        {
            var ps = eco.States[port];
            Uw2.Game.Engine.Town.Shipyard.EnterPort(Scenario.Memory, port, ps.Yard, ps.Industry, ps.Economy, st.ShipTypes, Roll);
        }
        // 항구에 든 함대는 그 앞바다에 매여 있다 — 출항 전에 세계지도를 열어도 (0, 0) 에 있지 않게 옮겨 둔다.
        if (Fleet != null && Ports != null && port < Ports.Ports.Count)
            Fleet.PlaceNearSea(Ports.Ports[port].X, Ports.Ports[port].Y);
        AtSea = false;
        _renderer.SetCells(_portMaps[port], PortMap.Size, PortMap.Size);
        _renderer.SetChips(_portSets[Math.Clamp(_portChipNo[port], 0, _portSets.Length - 1)]);
        _renderer.WrapX = false;
        _renderer.GridStep = 8;

        // 부두 언저리에 사람을 세운다. 배가 들어온 자리가 아래쪽 물가다.
        int setNo = Math.Clamp(_portChipNo[port], 0, _portSets.Length - 1);
        var bounds = _portBounds != null && setNo < _portBounds.Length ? _portBounds[setNo] : default;
        Walker = new Walker(_portMaps[port], bounds, _doors?.Of(port));
        if (!Walker.PlaceNear(PortMap.Size / 2.0, PortMap.Size * 0.72)) Walker = null;
        UseWalkerSprite(Walker != null);

        // 항구 화면의 눈금은 <b>칸</b>이다(칩 눈금이 아니다). 칸 하나를 열여섯 점으로 낸다.
        CellsPerPixel = 1.0 / ChipSheet.Size;
        if (Walker != null) Center = (Walker.X, Walker.Y);
        else FitToWindow();
        _dirty = true;
        return true;
    }

    /// <summary>지금 화면의 이름.</summary>
    public string SceneName =>
        CurrentPort < 0 ? "세계지도"
        : Ports != null && CurrentPort < Ports.Ports.Count ? Ports.Ports[CurrentPort].Name
        : $"항구 {CurrentPort}";

    private byte[]? _worldCells;
    private int _worldW, _worldH;
    private ChipSheet? _worldSheet;
    private PortMap? _portMaps;
    private ChipSheet[]? _portSets;
    private WalkBounds[]? _portBounds;
    private PortDoors? _doors;
    private PortChipNumbers? _portChipNo;

    /// <summary>온 지도가 창에 들어오게 배율을 맞춘다.</summary>
    public void FitToWindow()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double w = ActualWidth * dpi.DpiScaleX, h = ActualHeight * dpi.DpiScaleY;
        CellsPerPixel = Math.Max(_renderer.MapW / w, _renderer.MapH / h);
        Center = (_renderer.MapW / 2.0, _renderer.MapH / 2.0);
        _dirty = true;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowExW(0, WndClass, null, WsChild | WsVisible, 0, 0, 1, 1,
                                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                $"지도 창의 자식 창을 만들지 못했습니다 (Win32 {Marshal.GetLastWin32Error()})");
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        CompositionTarget.Rendering -= OnFrame;
        _backBufferView?.Dispose();
        _swapChain?.Dispose();
        _renderer.Dispose();
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    /// <summary>
    /// 자식 창이 지워졌으면(가려졌다 드러나거나 창을 옮겼을 때) 한 번은 다시 그린다.
    /// </summary>
    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmPaint = 0x000F;
        if (msg == WmPaint) _dirty = true;
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private void EnsureSwapChain(int w, int h)
    {
        if (_hwnd == IntPtr.Zero || w <= 0 || h <= 0) return;
        if (_swapChain != null && _pixelW == w && _pixelH == h) return;

        _backBufferView?.Dispose();
        _backBufferView = null;

        if (_swapChain == null)
        {
            using var dxgiDevice = _renderer.Device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            var desc = new SwapChainDescription1
            {
                Width = (uint)w,
                Height = (uint)h,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipDiscard,
                Scaling = Scaling.None,
            };
            _swapChain = factory.CreateSwapChainForHwnd(_renderer.Device, _hwnd, desc);
        }
        else
        {
            _swapChain.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        }

        using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferView = _renderer.Device.CreateRenderTargetView(back);
        _pixelW = w;
        _pixelH = h;
        _dirty = true;   // 새 백버퍼는 비어 있다 — 값이 같아도 한 번은 그려야 한다
    }

    private (double X, double Y) _lastOrigin;
    private double _lastDpiX = 1, _lastDpiY = 1;

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!_ready || _hwnd == IntPtr.Zero) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        EnsureSwapChain(w, h);
        if (_backBufferView == null) return;

        AdvanceFleet(w, h);
        if (!_dirty) return;

        _lastDpiX = dpi.DpiScaleX;
        _lastDpiY = dpi.DpiScaleY;
        var origin = (Center.X - w / 2.0 * CellsPerPixel, Center.Y - h / 2.0 * CellsPerPixel);
        _lastOrigin = origin;

        _renderer.ShowArrows = ShowArrows && CurrentPort < 0;
        _renderer.ShowOthers = CurrentPort < 0 && Others.Count > 0;
        if (_renderer.ShowOthers) PushOthers();
        _renderer.RenderTo(_backBufferView, w, h, origin, CellsPerPixel, ShipRectAt(origin));
        _swapChain!.Present(1, PresentFlags.None);
        _dirty = false;
        Painted?.Invoke();
    }

    /// <summary>
    /// 흐른 시간만큼 배를 옮긴다. 틱이 쌓인 만큼 <see cref="Fleet.Step"/> 을 되풀이한다.
    /// </summary>
    private void AdvanceFleet(int w, int h)
    {
        var now = _clock.Elapsed;
        double dt = Math.Min((now - _lastTick).TotalSeconds, 0.25);   // 창이 멈췄다 살아나도 안 튀게
        _lastTick = now;

        if (CurrentPort >= 0)
        {
            AdvanceWalker(dt, w, h);
            return;
        }
        if (Fleet == null || !Sailing || !AtSea || !Fleet.UnderSail) return;

        _carry += dt / Fleet.TickSeconds;
        int ticks = (int)_carry;
        if (ticks <= 0) return;
        _carry -= ticks;

        for (int i = 0; i < Math.Min(ticks, 20); i++)
        {
            int day = Fleet.DaysAtSea;
            Fleet.Step();
            // 틱마다 남 함대도 다 한 걸음 간다(0x0041A362). 바람 · 해류는 제 함대 자리의 것을 같이 쓴다.
            foreach (var npc in Others) npc.Step(Fleet.LastWind, Fleet.LastCurrent);
            if (Fleet.DaysAtSea != day) foreach (var npc in Others) npc.NewDay();
            _othersMoved = true;
        }
        _renderer.ShipFacesWest = Fleet.Heading > Fleet.DirCount / 2;
        FollowShip(w, h);
        _dirty = true;
    }

    /// <summary>도시에서 사람을 옮기고 따라간다.</summary>
    private void AdvanceWalker(double dt, int w, int h)
    {
        if (Walker == null) return;

        _carry += dt / Walker.TickSeconds;
        int ticks = (int)_carry;
        if (ticks <= 0) return;
        _carry -= ticks;

        // 누르고 있는 동안 걷는다 — 키를 떼면 선다. 키 눌림은 프레임마다 그때그때 본다.
        int ix = (Keyboard.IsKeyDown(Key.Right) ? 1 : 0) - (Keyboard.IsKeyDown(Key.Left) ? 1 : 0);
        int iy = (Keyboard.IsKeyDown(Key.Down) ? 1 : 0) - (Keyboard.IsKeyDown(Key.Up) ? 1 : 0);
        if (WalkInput != (0, 0)) (ix, iy) = WalkInput;
        bool before = Walker.Moving;
        for (int i = 0; i < Math.Min(ticks, 8); i++) Walker.Step_(ix, iy);

        // 문에 서면 그 건물에 든다 — 닫힌 시각이면 잠겨 있다. 건물 안은 아직 없어 알리기만 하고 한 칸 물러 나온다.
        // 도시의 시각은 바다에서 넘어온 함대 시각을 그대로 쓴다(도시 안에서 시각이 흐르는지는 아직 안 봤다).
        if (Walker.Moving && Walker.Building is int building and >= 0)
        {
            int tick = Fleet?.TimeOfDay ?? Fleet.MorningTick;
            BuildingEntered?.Invoke(building, PortDoors.IsOpen(building, tick));
            Walker.StepOutOfDoor();
        }

        // 사람이 가장자리에 다가오면 화면을 넘긴다. 자리는 칸 눈금이다.
        double px = Walker.X, py = Walker.Y;
        double halfW = w / 2.0 * CellsPerPixel, halfH = h / 2.0 * CellsPerPixel;
        double margin = FollowMargin * CellsPerPixel;

        double cx = Center.X, cy = Center.Y;
        if (px - cx > halfW - margin) cx = px - (halfW - margin);
        if (px - cx < -(halfW - margin)) cx = px + (halfW - margin);
        if (py - cy > halfH - margin) cy = py - (halfH - margin);
        if (py - cy < -(halfH - margin)) cy = py + (halfH - margin);
        Center = (cx, cy);

        if (Walker.Moving || before) _dirty = true;
    }

    /// <summary>배가 가장자리에 다가오면 화면을 넘긴다. 여백 안에서는 지도가 멈춰 있다.</summary>
    private void FollowShip(int w, int h)
    {
        if (Fleet == null) return;
        double halfW = w / 2.0 * CellsPerPixel, halfH = h / 2.0 * CellsPerPixel;
        double marginX = FollowMargin * CellsPerPixel, marginY = FollowMargin * CellsPerPixel;

        double dx = Fleet.X - Center.X;
        if (dx > ChipMap.Width / 2.0) dx -= ChipMap.Width;
        if (dx < -ChipMap.Width / 2.0) dx += ChipMap.Width;

        double cx = Center.X, cy = Center.Y;
        if (dx > halfW - marginX) cx += dx - (halfW - marginX);
        if (dx < -(halfW - marginX)) cx += dx + (halfW - marginX);

        double dy = Fleet.Y - Center.Y;
        if (dy > halfH - marginY) cy += dy - (halfH - marginY);
        if (dy < -(halfH - marginY)) cy += dy + (halfH - marginY);

        Center = (cx, cy);
    }

    /// <summary>배 그림이 놓일 화면 사각형. 안 그릴 때는 폭이 0 이다.</summary>
    private (float X, float Y, float W, float H) ShipRectAt((double X, double Y) origin)
    {
        if (CurrentPort >= 0) return WalkerRectAt(origin);
        if (Fleet == null || _shipArt.Length == 0) return default;

        double sx = Fleet.X - origin.X;
        if (sx < -ChipMap.Width / 2.0) sx += ChipMap.Width;
        if (sx > ChipMap.Width / 2.0) sx -= ChipMap.Width;

        double px = sx / CellsPerPixel;
        double py = (Fleet.Y - origin.Y) / CellsPerPixel;

        // 확대할수록 배도 커지되 너무 작아지지도 크지도 않게 잡는다.
        double scale = Math.Clamp(1.0 / CellsPerPixel / 2.0, 0.35, 2.5) * ShipScale;
        float dw = (float)(_shipW * scale), dh = (float)(_shipH * scale);
        return ((float)(px - dw / 2), (float)(py - dh), dw, dh);
    }

    /// <summary>사람 그림이 놓일 화면 사각형. 그림 판에서 지금 장을 잘라 쓴다.</summary>
    private (float X, float Y, float W, float H) WalkerRectAt((double X, double Y) origin)
    {
        if (Walker == null || _walkArt.Length == 0) return default;

        double px = (Walker.X - origin.X) / CellsPerPixel;
        double py = (Walker.Y - origin.Y) / CellsPerPixel;

        // 칸 하나가 화면 몇 점인지 나눠 그림 배수를 낸다 — 칸이 열여섯 점이면 원본 크기다.
        double scale = 1.0 / CellsPerPixel / ChipSheet.Size;

        float dw = (float)(Walker.SpriteW * scale), dh = (float)(Walker.SpriteH * scale);

        // 발이 밟은 칸에 닿게 놓는다. 자리는 칸 한가운데라 반 칸 아래가 칸 밑변이다.
        double foot = py + 0.5 / CellsPerPixel;

        _renderer.SpriteFrame(Walker.Frame, Walker.SpriteW, Walker.SpriteH, AssetPack.CharCols);
        return ((float)(px - dw / 2), (float)(foot - dh), dw, dh);
    }

    /// <summary>화면 점을 칸 좌표로.</summary>
    public (double X, double Y) CellAt(Point p) =>
        (_lastOrigin.X + p.X * _lastDpiX * CellsPerPixel,
         _lastOrigin.Y + p.Y * _lastDpiY * CellsPerPixel);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var cell = CellAt(e.GetPosition(this));
        HoverCell = ((int)Math.Floor(cell.X), (int)Math.Floor(cell.Y));

        if (_dragging)
        {
            var now = e.GetPosition(this);
            Center = (Center.X - (now.X - _dragFrom.X) * _lastDpiX * CellsPerPixel,
                      Center.Y - (now.Y - _dragFrom.Y) * _lastDpiY * CellsPerPixel);
            _dragFrom = now;
            _dirty = true;
        }
        Painted?.Invoke();
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _dragging = true;
            _dragFrom = e.GetPosition(this);
            CaptureMouse();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) { _dragging = false; ReleaseMouseCapture(); }
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // 커서가 짚은 칸이 제자리에 남게 늘이고 줄인다.
        var before = CellAt(e.GetPosition(this));
        double factor = e.Delta > 0 ? 1 / 1.25 : 1.25;
        CellsPerPixel = Math.Clamp(CellsPerPixel * factor, 1.0 / 64, 8.0);
        var after = CellAt(e.GetPosition(this));
        Center = (Center.X + before.X - after.X, Center.Y + before.Y - after.Y);
        _dirty = true;
        base.OnMouseWheel(e);
    }

    /// <summary>다음 프레임에 다시 그리라고 표시한다.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>뱃머리를 돌린다.</summary>
    public void Steer(int by)
    {
        if (Fleet == null) return;
        Fleet.Turn(by);
        _dirty = true;
    }

    /// <summary>뱃머리를 그 방위로 맞춘다.</summary>
    public void SteerTo(int dir)
    {
        if (Fleet == null) return;
        Fleet.Heading = ((dir % Fleet.DirCount) + Fleet.DirCount) % Fleet.DirCount;
        _dirty = true;
    }

    /// <summary>돛을 폈다 접었다 한다.</summary>
    public void ToggleSail()
    {
        if (Fleet == null) return;
        Fleet.UnderSail = !Fleet.UnderSail;
        _dirty = true;
    }
}
