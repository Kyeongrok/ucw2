using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Uw2.Game.Engine;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 시나리오 대사 창 — 한 줄씩 넘긴다(원본 <c>C8</c> 대사 · <c>C7</c> 기다림 자리). 누르거나 Enter · Space 로 다음.
/// </summary>
/// <remarks>
/// 얼굴(<c>CC</c>)이 있으면 곁에 찍는다. 원본은 위 창(C0 1)과 아래 창(C0 2)을 따로 띄우는데, 여기서는 한 창에서
/// 위 창이면 얼굴을 왼쪽, 아래 창이면 오른쪽에 둔다. 고르기(<c>C9</c>)는 대사를 다 넘긴 뒤 항목 단추를 띄우고,
/// 원본처럼 물릴 수 없다(Esc 를 받지 않는다).
/// </remarks>
public sealed class ScenarioDialog : Window
{
    private readonly IReadOnlyList<ScenarioVm.Line> _lines;
    private readonly IReadOnlyList<string> _options;
    private readonly FaceSheet? _faces;
    private readonly TextBlock _speaker = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xB0, 0x5A)),
        FontSize = 15,
        Margin = new Thickness(0, 0, 0, 6),
    };
    private readonly TextBlock _body = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xE2, 0xCE)),
        FontSize = 16,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 60,
    };
    private readonly TextBlock _page = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x78, 0x66)),
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly Image _face = new() { Width = 128, Height = 160, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 14, 0) };
    private readonly Image _picture = new() { Margin = new Thickness(18, 18, 18, 0), Visibility = Visibility.Collapsed };
    private readonly ImageSource? _fixedPicture;
    private readonly StackPanel _choices = new() { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Grid _row = new();
    private int _at;
    private string _lastSpeaker = "";

    /// <summary>고른 항목의 차례 — 고르기가 아니면 -1.</summary>
    public int Picked { get; private set; } = -1;

    private ScenarioDialog(FaceSheet? faces, IReadOnlyList<ScenarioVm.Line> lines, IReadOnlyList<string> options,
                           ImageSource? picture = null)
    {
        _faces = faces;
        _lines = lines;
        _options = options;
        Title = "대항해시대2";
        Width = 680;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x0C, 0x06));
        RenderOptions.SetBitmapScalingMode(_face, BitmapScalingMode.NearestNeighbor);

        var text = new StackPanel();
        text.Children.Add(_speaker);
        text.Children.Add(_body);
        text.Children.Add(_choices);
        text.Children.Add(_page);
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _row.ColumnDefinitions.Add(new ColumnDefinition());
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _row.Children.Add(_face);
        _row.Children.Add(text);
        Grid.SetColumn(text, 1);
        _row.Margin = new Thickness(18);
        // 그림(GRAPH.DAT 사건 그림 또는 시나리오 CB 그림) — 원본은 말 창 위에 찍는다. 두 배로, 점이 뭉개지지 않게.
        _fixedPicture = picture;
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.NearestNeighbor);
        SetPicture(picture);
        var all = new StackPanel();
        all.Children.Add(_picture);
        all.Children.Add(_row);
        Content = all;

        MouseLeftButtonUp += (_, _) => Next();
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) Next();
            else if (e.Key == Key.Escape && _options.Count == 0) Close();
        };
        if (_lines.Count > 0) Show(0); else ShowChoices();
    }

    private void Show(int i)
    {
        var line = _lines[i];
        // 말하는 사람이 안 적힌 줄은 앞사람이 이어 말하는 것이다.
        if (line.Speaker.Length > 0) _lastSpeaker = line.Speaker;
        _speaker.Text = _lastSpeaker;
        _body.Text = line.Body;
        _page.Text = $"{i + 1} / {_lines.Count}";
        if (_fixedPicture is null) SetPicture(line.Picture >= 0 ? _faces?.EventPictures?.Invoke(line.Picture) : null);
        var img = _faces?.Get(line.Face);
        _face.Source = img;
        _face.Visibility = img != null ? Visibility.Visible : Visibility.Collapsed;
        bool right = line.Window == 2;
        Grid.SetColumn(_face, right ? 2 : 0);
        _face.Margin = right ? new Thickness(14, 0, 0, 0) : new Thickness(0, 0, 14, 0);
    }

    private void SetPicture(ImageSource? picture)
    {
        _picture.Source = picture;
        _picture.Visibility = picture != null ? Visibility.Visible : Visibility.Collapsed;
        if (picture != null) { _picture.Width = picture.Width * 2; _picture.Height = picture.Height * 2; }
    }

    private void Next()
    {
        if (_choices.Visibility == Visibility.Visible) return;
        if (++_at >= _lines.Count)
        {
            if (_options.Count > 0) ShowChoices(); else Close();
            return;
        }
        Show(_at);
    }

    private void ShowChoices()
    {
        _page.Text = "";
        _choices.Visibility = Visibility.Visible;
        for (int i = 0; i < _options.Count; i++)
        {
            int pick = i;
            var b = new Button
            {
                Content = _options[i],
                FontSize = 15,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(0x30, 0x1C, 0x10)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xE2, 0xCE)),
            };
            b.Click += (_, _) => { Picked = pick; Close(); };
            _choices.Children.Add(b);
        }
        if (_choices.Children[0] is Button first) Dispatcher.BeginInvoke(() => first.Focus());
    }

    public static void Play(Window owner, FaceSheet? faces, IReadOnlyList<ScenarioVm.Line> lines, ImageSource? picture = null)
    {
        if (lines.Count == 0) return;
        new ScenarioDialog(faces, lines, [], picture) { Owner = owner }.ShowDialog();
    }

    /// <summary>앞 대사를 넘긴 뒤 항목을 고르게 한다 — 고른 차례(창을 닫아 버리면 0).</summary>
    public static int Ask(Window owner, FaceSheet? faces, IReadOnlyList<ScenarioVm.Line> before, IReadOnlyList<string> options)
    {
        if (options.Count == 0) return 0;
        var d = new ScenarioDialog(faces, before, options) { Owner = owner };
        d.ShowDialog();
        return Math.Max(0, d.Picked);
    }

    /// <summary>그림과 대사 뒤에 예 · 아니오를 묻는다 — 창을 닫아 버리면 아니오.</summary>
    public static bool Confirm(Window owner, FaceSheet? faces, IReadOnlyList<ScenarioVm.Line> before, ImageSource? picture = null)
    {
        var d = new ScenarioDialog(faces, before, ["예", "아니오"], picture) { Owner = owner };
        d.ShowDialog();
        return d.Picked == 0;
    }
}
