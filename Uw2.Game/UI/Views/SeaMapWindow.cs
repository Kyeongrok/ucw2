using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Uw2.Game.Engine.Sea;
using Uw2.Game.UI.Rendering;
using Uw2.Support.Local.Formats;
using Uw2.Support.Local.Helpers;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 세계지도를 띄우는 창. 끌어 옮기고 휠로 늘이고 줄인다.
/// </summary>
/// <remarks>
/// 배를 움직이는 것은 아직 없다 — 지도가 제대로 서는지 보는 것이 먼저다.
/// 3편 <c>ShipMapWindow</c> 자리다.
/// </remarks>
public sealed class SeaMapWindow : Window
{
    private readonly SeaMapHost _host = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 4, 8, 4) };
    private string _gameDir = "";
    private CheckBox _chipToggle = null!;
    private CheckBox _arrows = null!;

    /// <summary>마지막으로 든 건물 — 상태줄에 적는다. 항구를 옮기면 지운다.</summary>
    private string _doorNote = "";

    /// <summary>건물 번호 3 — 항구, 0 — 교역소(<see cref="PortDoors.Names"/>).</summary>
    private const int HarborBuilding = 3, TradeBuilding = 0;

    /// <summary>교역소 — 「구입」을 고르면 이 항구에서 파는 것과 값(<see cref="Economy.ForSale"/>)을 늘어놓는다. 사고팔기는 아직이다.</summary>
    private void Trade()
    {
        int pick = BuildingMenu.Ask(this, "교역소", BuildingMenu.Trade);
        if (pick == 1) { _doorNote = " · 팔 짐이 없습니다(짐칸은 아직 없습니다)"; UpdateStatus(); return; }
        if (pick != 0 || _host.Economy is not { } eco) return;
        var goods = eco.ForSale(_host.CurrentPort);
        if (goods.Count == 0) { _doorNote = " · 이 항구 교역소에는 파는 것이 없습니다"; UpdateStatus(); return; }
        BuildingMenu.Ask(this, "구입 — 한 통 값", goods.Select(g => $"{eco.GoodsNames[g.Good],-6}  {g.Price,5}").ToList());
    }

    /// <summary>항구 고르기 — 출항만 된다. 보급 · 명명은 아직이다.</summary>
    private void Harbor()
    {
        int pick = BuildingMenu.Ask(this, "항", BuildingMenu.Harbor);
        if (pick == 0) { PutToSea(); return; }
        if (pick > 0)
        {
            _doorNote = $" · 「{BuildingMenu.Harbor[pick].Replace("  ", "")}」은 아직 없습니다";
            UpdateStatus();
        }
    }

    /// <summary>항해사가 마지막으로 한 말(날씨) — 상태줄에 적는다.</summary>
    private string _seaNote = "";
    private ComboBox _portPick = null!;

    public SeaMapWindow()
    {
        Title = "대항해시대2 — 세계지도";
        Width = 1180;
        Height = 700;
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x1A));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        bar.Children.Add(Button("게임 폴더…", (_, _) => PickFolder()));
        bar.Children.Add(Button("세계지도", (_, _) => { _host.ShowWorld(); _portPick.SelectedIndex = -1; Retitle(); }));
        bar.Children.Add(Button("출항", (_, _) => PutToSea()));

        // 항구 고르개. 표를 못 읽으면 번호만 늘어놓는다.
        _portPick = new ComboBox
        {
            Width = 150,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _portPick.SelectionChanged += (_, _) =>
        {
            if (_portPick.SelectedIndex >= 0 && _host.ShowPort(_portPick.SelectedIndex)) Retitle();
        };
        bar.Children.Add(_portPick);
        bar.Children.Add(Toggle("조각 격자", v => { _host.Renderer.ShowGrid = v; _host.Invalidate(); }));
        _arrows = Toggle("바람·해류", v => { _host.ShowArrows = v; _host.Invalidate(); });
        bar.Children.Add(_arrows);
        _chipToggle = Toggle("칩으로 그리기", v => { _host.Renderer.UseChips = v; _host.Invalidate(); }, true);
        bar.Children.Add(_chipToggle);
        Grid.SetRow(bar, 0);
        grid.Children.Add(bar);

        Grid.SetRow(_host, 1);
        grid.Children.Add(_host);

        _status.Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xB4, 0xC0));
        Grid.SetRow(_status, 2);
        grid.Children.Add(_status);

        Content = grid;

        _host.Painted += UpdateStatus;
        _host.FleetReady += f => f.Weather.Said += line =>
        {
            _seaNote = $" · 「{line}」";
            UpdateStatus();
        };
        _host.BuildingEntered += (b, open) =>
        {
            _doorNote = open
                ? $" · 「{PortDoors.Names[b]}」에 들어갔습니다" + (b == HarborBuilding ? "" : "(안은 아직 없습니다)")
                : $" · 「{PortDoors.Names[b]}」 — {PortDoors.LockedText}";
            UpdateStatus();
            // 항구는 고르기 창을 띄운다 — 그림을 그리는 틈 안이라 한 박자 뒤에 연다.
            if (open && b == HarborBuilding) Dispatcher.BeginInvoke(Harbor);
            if (open && b == TradeBuilding) Dispatcher.BeginInvoke(Trade);
        };
        Loaded += (_, _) => OpenBest();
        KeyDown += OnKey;
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3) };
        b.Click += click;
        return b;
    }

    private static CheckBox Toggle(string text, Action<bool> set, bool on = false)
    {
        var c = new CheckBox { Content = text, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = on };
        c.Checked += (_, _) => set(true);
        c.Unchecked += (_, _) => set(false);
        return c;
    }

    /// <summary>
    /// 구워 둔 자산이 있으면 그것으로, 없으면 게임 폴더로 연다.
    /// </summary>
    private void OpenBest()
    {
        var dir = AssetPack.FindFolder();
        if (dir != null)
        {
            var pack = AssetPack.Load(dir);
            if (pack != null && _host.StartFromAssets(pack))
            {
                _gameDir = dir;
                _chipToggle.IsChecked = _host.Renderer.UseChips;
                FillPortList();
                _host.FitToWindow();
                _host.Invalidate();
                UpdateStatus();
                return;
            }
        }
        Open(GameFolder.Find());
    }

    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "대항해시대2 폴더" };
        if (dlg.ShowDialog(this) == true) Open(dlg.FolderName);
    }

    private void Open(string? dir)
    {
        if (dir == null) { _status.Text = GameFolder.Explain(); return; }
        if (!GameFolder.IsGameFolder(dir)) { _status.Text = GameFolder.Explain(dir); return; }

        _gameDir = dir;
        if (!_host.Start(dir)) { _status.Text = _host.Status; return; }

        _chipToggle.IsChecked = _host.Renderer.UseChips;
        FillPortList();
        _host.FitToWindow();
        _host.Invalidate();
        UpdateStatus();
    }

    /// <summary>항구 고르개를 채운다. 리스본이 0번이라 그것으로 시작한다.</summary>
    private void FillPortList()
    {
        _portPick.Items.Clear();
        var t = _host.Ports;
        int n = t?.Ports.Count ?? PortMap.Count;
        for (int i = 0; i < n; i++)
            _portPick.Items.Add(t != null && i < t.Ports.Count ? $"{i,3}  {t.Ports[i].Name}" : $"항구 {i}");
        _portPick.SelectedIndex = -1;
    }

    /// <summary>보고 있는 화면 이름을 제목에 얹는다.</summary>
    private void Retitle()
    {
        Title = $"대항해시대2 — {_host.SceneName}";
        _doorNote = "";
        _host.Invalidate();
        UpdateStatus();
    }

    /// <summary>
    /// 그 주인공의 시작 항구를 띄운다. 표에 이름이 있으면 그것으로 찾고, 없으면 번호로 간다.
    /// </summary>
    public void ShowHomePort(int hero)
    {
        // 고향은 SDATA.DAT 주인공 함대의 머문 항구(+0x26) 그대로다 — 에른스트는 암스테르담(33)이다.
        string[] homes = ["리스본", "세빌리아", "제노바", "이스탄불", "암스테르담", "런던"];
        string want = hero >= 0 && hero < homes.Length ? homes[hero] : homes[0];
        _host.SetHero(hero);

        int index = _host.Ports?.ByName(want)?.Index ?? PortTable.Lisbon;
        if (_host.ShowPort(index)) { _portPick.SelectedIndex = index; Retitle(); }

        // 항구를 한 번 보여 주고, 나가면 그 앞바다에서 배가 뜬다.
        _homePort = index;
    }

    private int _homePort = PortTable.Lisbon;

    /// <summary>항구에서 나와 앞바다에 배를 띄운다.</summary>
    private void PutToSea()
    {
        int from = _host.CurrentPort >= 0 ? _host.CurrentPort : _homePort;
        _host.ShowWorld();
        _portPick.SelectedIndex = -1;
        if (!_host.SailFrom(from)) _host.FitToWindow();
        _host.CellsPerPixel = 1.0 / 6;               // 칩 하나에 여섯 점 — 배가 잘 보인다
        _host.Invalidate();
        Retitle();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        double step = 20 * _host.CellsPerPixel;

        // 도시에서는 방향키가 사람을 걷게 한다 — 누르고 있는 동안 걷도록 호스트가 그때그때 본다.
        if (_host.CurrentPort >= 0 && _host.Walker != null &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            _host.Invalidate();
            e.Handled = true;
            return;
        }

        // 바다에 나와 있으면 방향키가 뱃머리를 잡는다.
        bool steering = _host.Sailing && _host.CurrentPort < 0 && _host.Fleet != null;
        if (steering)
        {
            switch (e.Key)
            {
                case Key.Left: _host.Steer(-1); e.Handled = true; return;
                case Key.Right: _host.Steer(+1); e.Handled = true; return;
                case Key.Up: _host.SteerTo(0); e.Handled = true; return;      // 북
                case Key.Down: _host.SteerTo(4); e.Handled = true; return;    // 남
                case Key.Space: _host.ToggleSail(); e.Handled = true; return;
            }
        }

        switch (e.Key)
        {
            case Key.Left: _host.Center = (_host.Center.X - step, _host.Center.Y); break;
            case Key.Right: _host.Center = (_host.Center.X + step, _host.Center.Y); break;
            case Key.Up: _host.Center = (_host.Center.X, _host.Center.Y - step); break;
            case Key.Down: _host.Center = (_host.Center.X, _host.Center.Y + step); break;
            case Key.G: _host.Renderer.ShowGrid = !_host.Renderer.ShowGrid; break;
            case Key.A: _arrows.IsChecked = !_host.ShowArrows; break;   // 바람·해류 화살표
            case Key.C: _host.Renderer.UseChips = !_host.Renderer.UseChips; break;
            case Key.F: _host.FitToWindow(); break;
            case Key.S: PutToSea(); return;
            case Key.W: _host.ShowWorld(); _portPick.SelectedIndex = -1; Retitle(); break;
            case Key.L:                                      // 리스본
                if (_host.ShowPort(PortTable.Lisbon)) { _portPick.SelectedIndex = PortTable.Lisbon; Retitle(); }
                break;
            default: return;
        }
        _host.Invalidate();
        e.Handled = true;
    }

    private void UpdateStatus()
    {
        if (_host.Map == null) return;

        if (_host.CurrentPort >= 0)
        {
            var p = _host.Ports != null && _host.CurrentPort < _host.Ports.Ports.Count
                ? _host.Ports.Ports[_host.CurrentPort] : default;
            var wk = _host.Walker;
            string who = wk != null
                ? $" · 사람 ({wk.X:F1}, {wk.Y:F1}) {wk.Face}" + (wk.Blocked ? " · 막힘" : "")
                : " · 걸을 데를 못 찾았습니다";
            _status.Text = $"{_host.SceneName} ({_host.CurrentPort}번) · {PortMap.Size}x{PortMap.Size} 칸"
                         + who + _doorNote + $" · {_host.Source}";
            return;
        }
        // 커서 자리는 칩 눈금이다. 칸은 그 절반이다.
        int chipX = _host.HoverCell.X, chipY = _host.HoverCell.Y;
        int cx = chipX / ChipMap.PerCell, cy = chipY / ChipMap.PerCell;
        bool inside = cx >= 0 && cx < WorldMap.Width && cy >= 0 && cy < WorldMap.Height;
        string cell = inside ? $"칸 ({cx}, {cy}) 값 {_host.Map[cx, cy]}" : "칸 —";

        string wind = "";
        if (_host.Wind != null && inside)
        {
            // 표는 칩 눈금으로 짚는다. 커서 자리에는 함대 날짜가 없어 게임 시작 달(4 = May)로 본다.
            var (dir, speed) = _host.Wind.Wind(4, chipX, chipY);
            var (cdir, cspeed) = _host.Wind.Current(chipX, chipY);
            wind = $" · 바람 {dir}/{speed} 해류 {cdir}/{cspeed}";
        }

        var f = _host.Fleet;
        string sail = "";
        if (f != null && _host.CurrentPort < 0)
        {
            sail = $" | {f.Date} {Fleet.Clock(f.TimeOfDay)} · {f.DaysAtSea}일째 · 「{_host.ShipName}」 ({f.X:F0},{f.Y:F0}) 뱃머리 {Fleet.DirName(f.Heading)}"
                 + $" · 바람 {Fleet.DirName(f.LastWind.Dir)} {f.LastWind.Speed}"
                 + $" · 해류 {Fleet.DirName(f.LastCurrent.Dir)} {f.LastCurrent.Speed}"
                 + $" · {Sailing.Knots(f.LastSpeed)}노트({f.LastSpeed})"
                 + (f.UnderSail ? "" : " · 돛 접음") + (f.Blocked ? " · 뭍" : "")
                 + (f.Weather.Label.Length > 0 ? $" · {f.Weather.Label}" : "") + _seaNote;
        }

        double lon = cx / (double)WorldMap.Width * 360 - 180;
        double lat = 90 - cy / (double)WorldMap.Height * 180;
        _status.Text = $"{cell}{wind} · 경도 {lon:F1} 위도 {lat:F1} · 칩당 {1 / _host.CellsPerPixel:F1}점"
                     + sail + $" · {_host.Source}";
    }
}
