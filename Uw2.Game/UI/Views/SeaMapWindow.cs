using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        Loaded += (_, _) => Open(GameFolder.Find());
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
        _host.Invalidate();
        UpdateStatus();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        double step = 20 * _host.CellsPerPixel;
        switch (e.Key)
        {
            case Key.Left: _host.Center = (_host.Center.X - step, _host.Center.Y); break;
            case Key.Right: _host.Center = (_host.Center.X + step, _host.Center.Y); break;
            case Key.Up: _host.Center = (_host.Center.X, _host.Center.Y - step); break;
            case Key.Down: _host.Center = (_host.Center.X, _host.Center.Y + step); break;
            case Key.G: _host.Renderer.ShowGrid = !_host.Renderer.ShowGrid; break;
            case Key.C: _host.Renderer.UseChips = !_host.Renderer.UseChips; break;
            case Key.F: _host.FitToWindow(); break;
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
            string at = p.Name.Length > 0 ? $" · 세계지도 칸 ({p.Cell.X}, {p.Cell.Y})" : "";
            _status.Text = $"{_host.SceneName} ({_host.CurrentPort}번) · {PortMap.Size}x{PortMap.Size} 칸"
                         + $"{at} · 칩당 {1 / _host.CellsPerPixel:F1}점 · {_gameDir}";
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
            // 표는 칩 눈금으로 짚는다. 달은 아직 붙일 데가 없어 4월(놀이 시작 달)로 본다.
            var (dir, speed) = _host.Wind.Wind(4, chipX, chipY);
            var (cdir, cspeed) = _host.Wind.Current(chipX, chipY);
            wind = $" · 바람 {dir}/{speed} 해류 {cdir}/{cspeed}";
        }

        double lon = cx / (double)WorldMap.Width * 360 - 180;
        double lat = 90 - cy / (double)WorldMap.Height * 180;
        _status.Text = $"{cell}{wind} · 경도 {lon:F1} 위도 {lat:F1} · 칩당 {1 / _host.CellsPerPixel:F1}점"
                     + $" · {GameFolder.BuildName(_host.Build)} · {_host.ChipNote} · {_gameDir}";
    }
}
