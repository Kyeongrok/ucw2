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
/// 주인공 판에는 얼굴(<c>KAO.LZW</c>, <see cref="KaoImages"/>)을 찍는다. 얼굴 차례는 사람 표 차례
/// (조안 · 카탈리나 · 옷토 · 에르네스트 · 피에트로 · 알)라 이 창의 차례와 다르다(<see cref="HeroFace"/>).
/// 구운 얼굴이 없으면 이름을 적은 판을 놓는다.
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
        ("Ernst von Bohr",   "암스테르담"),    // SDATA 주인공 함대 50 의 머문 항구 33
        ("Otto Baynes",      "런던"),
    ];

    /// <summary>이 창의 주인공 차례 → 얼굴 번호(= 사람 표 번호).</summary>
    private static readonly int[] HeroFace = [0, 1, 4, 5, 3, 2];

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

    /// <summary>「Load Data」에서 세이브를 골랐을 때.</summary>
    public event Action<Uw2.Game.Engine.SaveGame>? SaveChosen;

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

    private GameMessages? _messages;

    /// <summary>
    /// 고른 주인공의 신상(<c>0x00416770</c>) — 연령(사람 +0x2A) · 능력치 일곱(+0x1C ~) 과 소개 말(<c>0x32 + 사람 번호</c>, 표 <c>0x00457FB8</c>)을
    /// 보이고 이 주인공으로 나갈지 묻는다. 원본은 이 뒤에 이름 바꾸기(0x3AC)를 묻는다 — 우리는 바다 창이 묻는다.
    /// </summary>
    private bool ConfirmHero(int pick)
    {
        int person = HeroFace[pick];
        var start = _pack?.Start;
        var raw = start?.PeopleBytes;
        int at = person * StartData.PersonSize;
        if (start == null || raw == null || at + StartData.PersonSize > raw.Length) return true;
        var p = start.People[person];
        string[] names = ["통솔력", "항해술", "지식", "직감", "용기", "검술", "매력"];
        string stats = string.Join("  ", names.Select((n, i) => $"{n} {raw[at + 0x1C + i]}"));
        string intro = _messages?[0x32 + person] ?? "";
        string text = $"{p.Name} {p.Family}  —  연령 {raw[at + 0x2A]}세\n{stats}\n\n{intro}\n\n이 주인공으로 출항하겠습니까？";
        return MessageBox.Show(this, text, "주인공", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    }

    private void LoadAssets()
    {
        PortTable.RegisterEncodings();
        var dir = AssetPack.FindFolder();
        _pack = dir != null ? AssetPack.Load(dir) : null;
        string msgPath = dir != null ? Path.Combine(dir, "snr", "MESSAGE.DAT") : "";
        _messages = File.Exists(msgPath) ? GameMessages.Load(msgPath) : null;
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

            if (_pack is { Faces.Length: > 0 } pack)
            {
                // 얼굴은 64 x 80 이고 판 안쪽은 그보다 조금 작다 — 테두리 안으로 들어오게 가장자리를 자른다.
                var (sx, sy) = AssetPack.FaceAt(HeroFace[i]);
                int cutX = (KaoImages.FaceW - (PlateW - 6)) / 2, cutY = (KaoImages.FaceH - (PlateH - 6)) / 2;
                _screen.Blit(pack.Faces, pack.FacesWidth, sx + cutX, sy + cutY, PlateW - 6, PlateH - 6,
                             px + 3, py + 3, _palette);
            }
            else
            {
                var words = name.Split(' ');
                int ty = py + (PlateH - words.Length * 17) / 2 - 6;
                for (int k = 0; k < words.Length; k++)
                    _screen.TextCentered(words[k], px + PlateW / 2, ty + k * 17, Cream);
            }
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

    /// <summary>원본 세이브 파일을 골라 그 칸 하나를 들인다(<see cref="Uw2.Support.Local.Formats.OriginalSave"/>).</summary>
    private void LoadOriginal()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "원본 세이브", Filter = "KOUKAI2.DAT|KOUKAI2.DAT|DAT 파일|*.DAT" };
        if (dlg.ShowDialog(this) != true) return;
        Uw2.Support.Local.Formats.OriginalSave file;
        try { file = Uw2.Support.Local.Formats.OriginalSave.Load(dlg.FileName); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, e.Message, "원본 세이브");
            return;
        }
        var slots = file.Slots.Where(s => s != null).Select(s => s!).ToList();
        if (slots.Count == 0) { MessageBox.Show(this, "들일 칸이 없습니다.", "원본 세이브"); return; }
        string[] heroes = ["조안", "카탈리나", "옷토", "에르네스트", "피에트로", "알"];
        int pick = BuildingMenu.Ask(this, "원본 세이브 — 칸을 고르세요",
            [.. slots.Select(s => $"{s.Number + 1}. {s.Label} · {(s.HeroPerson < heroes.Length ? heroes[s.HeroPerson] : "?")}")]);
        if (pick < 0) return;
        SaveChosen?.Invoke(new Uw2.Game.Engine.SaveGame { Original = slots[pick], Port = slots[pick].Port });
    }

    private void Choose()
    {
        if (_scene == Scene.Menu)
        {
            switch (_pick)
            {
                case 0:
                {
                    var lines = Uw2.Game.Engine.SaveGame.SlotLines().ToList();
                    lines.Add("원본 세이브(KOUKAI2.DAT) 불러오기…");
                    // 원본 말(0x3B7 · 0x3B8 · 0x3B9, 0x0041533B ~ 0x0041590C) — 빈 칸이면 알리고, 아니면 다짐받는다.
                    int slot = BuildingMenu.Ask(this, "데이터를 선택해 주세요.", lines);
                    if (slot == lines.Count - 1) { LoadOriginal(); return; }
                    if (slot < 0) return;
                    if (Uw2.Game.Engine.SaveGame.Read(slot) is not { } save)
                    {
                        MessageBox.Show(this, "데이터가 기록되어 있지 않습니다.", "불러오기");
                        return;
                    }
                    if (MessageBox.Show(this, "로드하겠습니다. 좋습니까？", "불러오기", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        SaveChosen?.Invoke(save);
                    return;
                }
                case 1: _scene = Scene.Characters; _pick = 0; Draw(); return;
                case 2: new SeaMapWindow().Show(); return;   // Environment 자리 — 지도를 연다
                case 3: Close(); return;
            }
        }
        else if (ConfirmHero(_pick))
        {
            HeroChosen?.Invoke(_pick);
        }
    }
}
