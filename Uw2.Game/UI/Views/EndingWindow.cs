using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 엔딩 차례 — 저장을 물은 뒤(<c>0x004043D7</c> → <c>0x00404700</c>) <c>END.DAT</c> 그림을 늘어놓는다.
/// </summary>
/// <remarks>
/// 원본은 항해일지와 모래시계(<c>0x00404A60</c>), 장면 일곱(<c>0x00405440</c>, <c>0x34B20</c> 부터), 주인공 그림(<c>0x00405B50</c>, 눈 · 촛불이 움직인다),
/// 「Fin」 을 차례로 그린다. 자리 · 때 · 빛깔 넘김은 아직 다 읽지 않아, 여기서는 큰 그림을 몇 초씩 넘긴다.
/// 장면은 테두리(640 x 400) 가운데에 놓는다. 아무 글쇠나 누름으로 건너뛴다.
/// </remarks>
public sealed class EndingWindow : Window
{
    private const double Scale = 1.5;
    private const int ScreenW = 640, ScreenH = 400;

    private readonly GraphSheet _graphs;
    private readonly int _hero;
    private readonly Canvas _canvas = new() { Width = ScreenW, Height = ScreenH, Background = Brushes.Black, ClipToBounds = true };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private readonly DispatcherTimer _flicker = new() { Interval = TimeSpan.FromSeconds(0.5) };
    private readonly List<Action> _steps = [];
    private int _at = -1;
    private Image? _glass;
    private bool _glassFrame;

    private EndingWindow(GraphSheet graphs, int hero)
    {
        _graphs = graphs;
        _hero = hero;
        Title = "대항해시대2";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;
        _canvas.LayoutTransform = new ScaleTransform(Scale, Scale);
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.NearestNeighbor);
        Content = _canvas;

        _steps.Add(() =>
        {
            Put("book", (ScreenW - 640) / 2, 50);
            _glass = Put("glass0", 456, 60);
            _flicker.Start();
        });
        for (int i = 0; i < 7; i++)
        {
            int k = i;
            _steps.Add(() => { Put("frame", 0, 0); Center($"scene{k}"); });
        }
        _steps.Add(HeroPicture);
        _steps.Add(() => Center("fin"));

        _timer.Tick += (_, _) => Next();
        _flicker.Tick += (_, _) =>
        {
            if (_glass == null) return;
            _glassFrame = !_glassFrame;
            _glass.Source = _graphs.EndingPicture(_glassFrame ? "glass1" : "glass0") ?? _glass.Source;
        };
        KeyDown += (_, _) => Close();
        MouseLeftButtonUp += (_, _) => Close();
        Closed += (_, _) => { _timer.Stop(); _flicker.Stop(); _frames.Stop(); };
        Next();
        _timer.Start();
    }

    /// <summary>
    /// 주인공 그림 — 눈은 이따금 깜빡이고(장 0 → 1 → 2 → 1) 촛불은 늘 흔들린다(<see cref="Npk.HeroEndingFrames"/>).
    /// </summary>
    private void HeroPicture()
    {
        if (_graphs.Ending(_hero) is not { } img) return;
        double ox = (ScreenW - img.Width) / 2.0, oy = (ScreenH - img.Height) / 2.0;
        Add(img, ox, oy);
        if (_hero < 0 || _hero >= Npk.HeroEndingFrames.Length) return;
        var (_, ex, ey, candle, cx, cy) = Npk.HeroEndingFrames[_hero];
        var eye = Put($"eye{_hero}-0", ox + ex, oy + ey);
        var flame = candle != null ? Put($"candle{_hero}-0", ox + cx, oy + cy) : null;
        int tick = 0;
        int[] blink = [0, 1, 2, 1];
        _frames.Tick += (_, _) =>
        {
            tick++;
            if (flame != null) flame.Source = _graphs.EndingPicture($"candle{_hero}-{tick % 3}") ?? flame.Source;
            if (eye != null)
            {
                int b = tick % 16;   // 열여섯 틱에 한 번 깜빡인다
                eye.Source = _graphs.EndingPicture($"eye{_hero}-{(b < blink.Length ? blink[b] : 0)}") ?? eye.Source;
            }
        };
        _frames.Start();
    }

    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromSeconds(0.15) };

    private Image? Put(string name, double x, double y) =>
        _graphs.EndingPicture(name) is { } img ? Add(img, x, y) : null;

    private void Center(string name)
    {
        if (_graphs.EndingPicture(name) is { } img) Add(img, (ScreenW - img.Width) / 2, (ScreenH - img.Height) / 2);
    }

    private Image Add(ImageSource img, double x, double y)
    {
        var view = new Image { Source = img, Width = img.Width, Height = img.Height };
        Canvas.SetLeft(view, x); Canvas.SetTop(view, y);
        _canvas.Children.Add(view);
        return view;
    }

    private void Next()
    {
        _flicker.Stop();
        _frames.Stop();
        _glass = null;
        if (++_at >= _steps.Count) { Close(); return; }
        _canvas.Children.Clear();
        _steps[_at]();
    }

    /// <summary>엔딩 차례를 돌린다 — 그림이 없으면(자산이 옛것) 아무것도 안 한다.</summary>
    public static void Play(Window? owner, GraphSheet graphs, int hero)
    {
        if (graphs.EndingPicture("book") is null) return;
        new EndingWindow(graphs, hero) { Owner = owner }.ShowDialog();
    }
}
