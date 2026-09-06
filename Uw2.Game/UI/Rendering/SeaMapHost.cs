using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    /// <summary>
    /// 구워 둔 자산으로 연다. <b>게임이 없어도 된다</b> — 3편 <c>asset/</c> 과 같은 얼개다.
    /// </summary>
    public bool StartFromAssets(AssetPack pack)
    {
        Map = WorldMap.FromCells(pack.Cells);
        Chips = pack.ToChipMap();
        Wind = pack.Wind.Length > 0 ? WindCurTable.FromBytes(pack.Wind) : null;

        var cells = Chips.Expand(Map);
        if (!InitRenderer(cells, ChipMap.Width, ChipMap.Height)) return false;

        Sheet = ChipSheet.FromAtlas(pack.WorldAtlas, pack.WorldAtlasSize.W, pack.WorldAtlasSize.H,
                                    AssetPack.AtlasCols, ChipSheet.Packing.Packed4);
        _renderer.SetChips(Sheet);
        _renderer.UseChips = true;
        ChipNote = $"칩 {Sheet.Count}장 · 구운 자산";

        _portMaps = pack.PortMaps.Length > 0 ? PortMap.FromCells(pack.PortMaps) : null;
        _portChipNo = pack.PortChipSet.Length > 0 ? PortChipNumbers.FromBytes(pack.PortChipSet) : null;
        _portSets = pack.PortAtlases.Length > 0
            ? [.. pack.PortAtlases.Select(a => ChipSheet.FromAtlas(
                  a, pack.PortAtlasSize.W, pack.PortAtlasSize.H,
                  AssetPack.AtlasCols, ChipSheet.Packing.Packed4))]
            : null;
        Ports = pack.Ports.Count > 0 ? PortTable.FromPorts(pack.Ports) : null;

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
            _portSets = PortChipSets.Load(Path.Combine(gameDir, "PORTCHIP.LZW")).All;
            _portChipNo = PortChipNumbers.Load(Path.Combine(gameDir, "CHIP_NO.DAT"));
            Ports = PortTable.Load(gameDir);
        }
        catch
        {
            _portMaps = null; _portSets = null; _portChipNo = null; Ports = null;
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
            Status = $"Direct3D 장치를 만들지 못했습니다 — {ex.Message}";
            return false;
        }
    }

    private bool Finish()
    {
        _worldCells = _renderer.Cells;
        _worldW = _renderer.MapW;
        _worldH = _renderer.MapH;
        _worldSheet = Sheet;
        _ready = true;
        _dirty = true;
        CompositionTarget.Rendering += OnFrame;
        return true;
    }

    /// <summary>어디서 읽었는지 — 게임 폴더 아니면 「구운 자산」.</summary>
    public string Source { get; private set; } = "";

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
        _renderer.SetCells(_portMaps[port], PortMap.Size, PortMap.Size);
        _renderer.SetChips(_portSets[Math.Clamp(_portChipNo[port], 0, _portSets.Length - 1)]);
        _renderer.WrapX = false;
        _renderer.GridStep = 8;
        FitToWindow();
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
        if (!_dirty) return;

        _lastDpiX = dpi.DpiScaleX;
        _lastDpiY = dpi.DpiScaleY;
        var origin = (Center.X - w / 2.0 * CellsPerPixel, Center.Y - h / 2.0 * CellsPerPixel);
        _lastOrigin = origin;

        _renderer.RenderTo(_backBufferView, w, h, origin, CellsPerPixel);
        _swapChain!.Present(1, PresentFlags.None);
        _dirty = false;
        Painted?.Invoke();
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
}
