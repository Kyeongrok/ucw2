using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 여는 화면 — <c>OPEN.DAT</c> 그림을 장면마다 늘어놓는다.
/// </summary>
/// <remarks>
/// 원본(<c>0x00431930</c>)은 장면 손 열둘(<c>0x00459B38</c>)을 차례로 부르고, 어느 하나가 글쇠를 받아 참을 돌려주면 나머지를 건너뛴다.
/// 장면마다 눈 깜빡임 · 별 반짝임 같은 조각 움직임이 있지만 여기서는 장면의 큰 그림만 찍고 몇 초씩 넘긴다.
/// 아무 글쇠나 누름으로 통째로 건너뛴다.
/// </remarks>
public sealed class OpeningWindow : Window
{
    /// <summary>장면 하나 — (그림 번호, 640 x 400 화면의 x, y).</summary>
    private static readonly (int N, int X, int Y)[][] Scenes =
    [
        [(0, 216, 120), (1, 216, 256)],                       // 0x0042E290 KOEI PRESENTS
        [(2, 184, 140), (3, 120, 220)],                       // 0x0042E420 리코에이션 게임 · 후쿠자와 에이지
        [(8, 0, 120), (18, 32, 300)],                         // 0x0042EB30 大航海時代II · 「Here begins …」
        [(34, 0, 0), (35, 528, 16), (19, 344, 184)],          // 0x0042F0C0 옷토
        [(39, 0, 0), (20, 344, 216)],                         // 0x0042F730 카탈리나
        [(64, 0, 0), (65, 512, 16), (21, 296, 216)],          // 0x0042FC50 에르네스트
        [(69, 0, 0), (70, 528, 16), (22, 296, 200)],          // 0x00430050 피에트로
        [(44, 0, 0), (45, 528, 16), (23, 328, 152)],          // 0x004304A0 알
        [(54, 0, 0), (24, 312, 320)],                         // 0x00430930 소녀들
        [(89, 0, 0), (25, 312, 288)],                         // 0x00430E00 에제키엘 · 마르티네스
        [(74, 0, 0), (75, 472, 16), (26, 328, 192)],          // 0x00431150 조안
        [(16, 0, 60), (27, 132, 300)],                        // 0x00431690 「In the name of glory …」
    ];

    private const double Scale = 1.5;
    private const int ScreenW = 640, ScreenH = 400;

    private readonly GraphSheet _graphs;
    private readonly Canvas _canvas = new() { Width = ScreenW, Height = ScreenH, Background = Brushes.Black, ClipToBounds = true };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private int _at = -1;

    private OpeningWindow(GraphSheet graphs)
    {
        _graphs = graphs;
        Title = "대항해시대2";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;
        _canvas.LayoutTransform = new ScaleTransform(Scale, Scale);
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.NearestNeighbor);
        Content = _canvas;
        _timer.Tick += (_, _) => Next();
        KeyDown += (_, _) => Close();
        MouseLeftButtonUp += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
        Next();
        _timer.Start();
    }

    private void Next()
    {
        if (++_at >= Scenes.Length) { Close(); return; }
        _canvas.Children.Clear();
        foreach (var (n, x, y) in Scenes[_at])
        {
            if (_graphs.Opening(n) is not { } img) continue;
            var view = new Image { Source = img, Width = img.Width, Height = img.Height };
            Canvas.SetLeft(view, x); Canvas.SetTop(view, y);
            _canvas.Children.Add(view);
        }
        if (_canvas.Children.Count == 0) Close();      // 그림이 없으면(자산이 옛것) 바로 끝낸다
    }

    /// <summary>여는 화면을 돌린다 — 그림이 없으면 아무것도 안 한다.</summary>
    public static void Play(GraphSheet graphs)
    {
        if (graphs.Opening(0) is null) return;
        new OpeningWindow(graphs).ShowDialog();
    }
}
