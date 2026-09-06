using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Uw2.Support.Local.Formats;
using Uw2.Support.Local.Helpers;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 게임을 열면 처음 나오는 창. <b>첫 메뉴</b>와 <b>주인공 고르기</b>를 여기서 다 한다.
/// </summary>
/// <remarks>
/// 원본 VGA 640x400 에 그리고 통째로 늘여 띄운다(<see cref="PixelScreen"/>). 글자는
/// <c>HANKAKU.FNT</c> 를 그대로 찍는다 — 원본 메뉴에 쓰인 그 글꼴이다.
///
/// <b>주인공 얼굴(<c>KAO.LZW</c>)은 아직 못 풀었다.</b> 청크가 1,920 · 864바이트 두 가지로
/// 128명 x 2쪽인데, 겹판·꽉채움에 48x80·64x60·80x48 따위를 다 대 봐도 어긋난다. 세계지도처럼
/// <b>한 겹 더</b> 있는 듯하다. 그때까지는 얼굴 자리에 이름을 적은 판을 놓는다.
/// </remarks>
public sealed class TitleWindow : Window
{
    private enum Scene { Menu, Characters }

    /// <summary>첫 메뉴의 줄들. 원본 그대로다.</summary>
    private static readonly string[] MenuItems =
        ["Load Data", "Start New Game", "Environment", "Quit Game"];

    /// <summary>
    /// 고를 수 있는 주인공 여섯. 자리는 <b>고향 항구를 항구표에서 찾아</b> 잡는다.
    /// </summary>
    private static readonly (string Name, string Home)[] Heroes =
    [
        ("Joao Franco",      "리스본"),
        ("Catalina Erantzo", "세빌리아"),
        ("Pietro Conti",     "제노바"),
        ("Ali Vezas",        "이스탄불"),
        ("Ernst von Bohr",   "함부르크"),
        ("Otto Baynes",      "런던"),
    ];

    /// <summary>얼굴 판 크기.</summary>
    private const int PlateW = 62, PlateH = 78;

    /// <summary>바탕 지도가 보여 줄 자리(칩 눈금). 리스본에서 이스탄불까지가 들어온다.</summary>
    private const int MapFromX = 40, MapFromY = 236;

    /// <summary>주인공 판이 놓일 자리. 고향 항구 위에 얹되 화면 안으로 끌어당긴다.</summary>
    private (int X, int Y)[] _plates = [];

    private void LayOutPlates()
    {
        _plates = new (int, int)[Heroes.Length];
        for (int i = 0; i < Heroes.Length; i++)
        {
            var found = _ports?.ByName(Heroes[i].Home);
            int px = found?.X ?? (MapFromX + 100 + i * 70);
            int py = found?.Y ?? (MapFromY + 60 + i * 40);

            int x = Math.Clamp(px - MapFromX - PlateW / 2, 14, PixelScreen.Width - PlateW - 14);
            int y = Math.Clamp(py - MapFromY - PlateH / 2, 14, PixelScreen.Height - PlateH - 30);
            _plates[i] = (x, y);
        }

        // 겹치면 아래로 밀어 준다 — 리스본과 세빌리아처럼 붙어 있는 데가 있다.
        for (int i = 1; i < _plates.Length; i++)
            for (int k = 0; k < i; k++)
                if (Math.Abs(_plates[i].X - _plates[k].X) < PlateW &&
                    Math.Abs(_plates[i].Y - _plates[k].Y) < PlateH)
                    _plates[i].Y = Math.Min(PixelScreen.Height - PlateH - 30, _plates[k].Y + PlateH + 4);
    }

    private PortTable? _ports;

    private readonly PixelScreen _screen = new();
    private readonly Image _view = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Scene _scene = Scene.Menu;
    private int _pick;
    private AssetPack? _pack;
    private uint[] _palette = GamePalette.SeaScreen().Bgra;

    /// <summary>고른 주인공이 정해지면 부른다 — 붙이는 쪽이 지도를 연다.</summary>
    public event Action<int>? HeroChosen;

    public TitleWindow()
    {
        Title = "대항해시대2";
        Width = 1024;
        Height = 680;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.NearestNeighbor);
        _view.Source = _screen.Source;
        Content = _view;

        Loaded += (_, _) => { LoadAssets(); Draw(); };
        KeyDown += OnKey;
        MouseLeftButtonUp += (_, e) => OnClick(e.GetPosition(_view));
    }

    private void LoadAssets()
    {
        PortTable.RegisterEncodings();
        var dir = AssetPack.FindFolder();
        _pack = dir != null ? AssetPack.Load(dir) : null;
        _screen.Font = _pack?.Font;
        _ports = _pack != null && _pack.Ports.Count > 0 ? PortTable.FromPorts(_pack.Ports) : null;
        LayOutPlates();

        if (_screen.Font == null)
        {
            // 구운 자산이 없으면 게임 폴더에서 글꼴만이라도 가져온다.
            var game = GameFolder.Find();
            if (game != null)
            {
                string fnt = Path.Combine(game, "HANKAKU.FNT");
                if (File.Exists(fnt)) _screen.Font = GameFont.Load(fnt);
            }
        }
        _palette = GamePalette.SeaScreen().Bgra;
    }

    // 원본 첫 화면의 색 — 검은 바탕에 크림빛 판, 주황·초록 테두리다.
    private const uint Black = 0xFF000000, Cream = 0xFFF2E2CE, Orange = 0xFFD2591E,
                       Green = 0xFF1E7A3C, Ink = 0xFF201008, Sea = 0xFF17376F;

    private void Draw()
    {
        if (_scene == Scene.Menu) DrawMenu(); else DrawCharacters();
        _screen.Present();
    }

    private void DrawMenu()
    {
        _screen.Clear(Black);

        const int x = 88, y = 58, w = 464, h = 256;
        _screen.GameFrame(x, y, w, h, Orange, Green, Cream);

        if (_screen.Font == null)
        {
            // 글꼴을 못 찾았을 때라도 뭐가 잘못됐는지는 보여야 한다.
            _screen.Fill(x + 20, y + 20, w - 40, 24, Orange);
            return;
        }

        int cx = x + w / 2;
        for (int i = 0; i < MenuItems.Length; i++)
        {
            int ty = y + 34 + i * 56;
            bool on = i == _pick;
            if (on) _screen.Fill(x + 24, ty - 8, w - 48, 48, Ink);
            _screen.TextCentered(MenuItems[i], cx, ty, on ? Cream : Ink, 2, 8);
        }
    }

    private void DrawCharacters()
    {
        // 바탕은 우리 지도다 — 원본도 유럽·지중해를 깔았다.
        DrawEurope();

        const int fx = 8, fy = 8, fw = PixelScreen.Width - 16, fh = PixelScreen.Height - 16;
        _screen.Rect(fx, fy, fw, fh, Orange, 2);
        _screen.Rect(fx + 3, fy + 3, fw - 6, fh - 6, Cream, 1);

        for (int i = 0; i < Heroes.Length; i++)
        {
            var (name, home) = Heroes[i];
            var (px, py) = _plates[i];
            bool on = i == _pick;

            _screen.Fill(px + 4, py + 4, PlateW, PlateH, Black);          // 그림자
            _screen.Fill(px, py, PlateW, PlateH, Ink);
            _screen.Rect(px, py, PlateW, PlateH, on ? Green : Orange, 3);

            // 얼굴(KAO.LZW)을 아직 못 풀어 이름을 대신 적는다.
            var words = name.Split(' ');
            int ty = py + (PlateH - words.Length * 17) / 2 - 6;
            for (int k = 0; k < words.Length; k++)
                _screen.TextCentered(words[k], px + PlateW / 2, ty + k * 17, Cream);
            _screen.TextCentered(home, px + PlateW / 2, py + PlateH - 20, on ? Green : Orange);
        }

        _screen.Fill(0, PixelScreen.Height - 22, PixelScreen.Width, 22, Black);
        _screen.TextCentered("Arrow keys to choose,  Enter to sail,  Esc to go back",
                             PixelScreen.Width / 2, PixelScreen.Height - 19, Cream);
    }

    /// <summary>유럽·지중해를 깔아 준다. 자산이 없으면 바다색으로 채운다.</summary>
    private void DrawEurope()
    {
        _screen.Clear(Sea);
        if (_pack == null) return;

        var chips = _pack.WorldAtlas;
        int aw = _pack.WorldAtlasSize.W;
        var chipMap = _pack.ToChipMap();
        var map = WorldMap.FromCells(_pack.Cells);

        // 칩 하나에 화면 한 점 — 리스본(120)에서 이스탄불(352)까지가 넉넉히 들어온다.
        for (int y = 0; y < PixelScreen.Height; y++)
            for (int x = 0; x < PixelScreen.Width; x++)
            {
                int cxp = MapFromX + x, cyp = MapFromY + y;
                var (lt, rt, lb, rb) = chipMap.Quad(map[cxp / ChipMap.PerCell, cyp / ChipMap.PerCell]);
                byte chip = ((cxp & 1) == 0) ? ((cyp & 1) == 0 ? lt : lb)
                                             : ((cyp & 1) == 0 ? rt : rb);
                int ox = (chip % 16) * ChipSheet.Size, oy = (chip / 16) * ChipSheet.Size;
                int idx = (oy + y % ChipSheet.Size) * aw + ox + x % ChipSheet.Size;
                _screen.Dot(x, y, _palette[idx >= 0 && idx < chips.Length ? chips[idx] & 0xF : 0]);
            }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        int n = _scene == Scene.Menu ? MenuItems.Length : Heroes.Length;
        switch (e.Key)
        {
            case Key.Up: case Key.Left: _pick = (_pick - 1 + n) % n; break;
            case Key.Down: case Key.Right: _pick = (_pick + 1) % n; break;
            case Key.Enter: case Key.Space: Choose(); return;
            case Key.Escape:
                if (_scene == Scene.Characters) { _scene = Scene.Menu; _pick = 1; break; }
                return;
            default: return;
        }
        Draw();
        e.Handled = true;
    }

    private void OnClick(Point p)
    {
        if (_view.ActualWidth <= 0) return;
        double sx = PixelScreen.Width / _view.ActualWidth, sy = PixelScreen.Height / _view.ActualHeight;
        int x = (int)(p.X * sx), y = (int)(p.Y * sy);

        if (_scene == Scene.Menu)
        {
            for (int i = 0; i < MenuItems.Length; i++)
                if (y >= 58 + 26 + i * 56 && y < 58 + 26 + i * 56 + 48) { _pick = i; Choose(); return; }
        }
        else
        {
            for (int i = 0; i < Heroes.Length; i++)
            {
                var (hx, hy) = _plates[i];
                if (x >= hx && x < hx + PlateW && y >= hy && y < hy + PlateH)
                {
                    if (_pick == i) { Choose(); return; }
                    _pick = i; Draw(); return;
                }
            }
        }
    }

    private void Choose()
    {
        if (_scene == Scene.Menu)
        {
            switch (_pick)
            {
                case 0:                                   // Load Data — 세이브는 아직이다
                case 1: _scene = Scene.Characters; _pick = 0; Draw(); return;
                case 2: new SeaMapWindow().Show(); return;   // Environment 자리 — 지도를 연다
                case 3: Close(); return;
            }
        }
        else
        {
            HeroChosen?.Invoke(_pick);
        }
    }
}
