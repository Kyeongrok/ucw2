using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Uw2.Game.Engine.Sea;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 함대 목록 26 의 「항해도」 · 「항구맵」 — 지리 조사 판(<see cref="Survey"/>)으로 가린 세계지도.
/// </summary>
/// <remarks>
/// 원본 「항해도」(<c>0x004437E0</c>)는 조사 판 90 x 45 를 띄우고 커서 칸 둘레 2 x 2 에 칠한 칸이 하나라도 있으면 그 언저리 지도를
/// 크게 보인다. 여기서는 세계지도 칸(1080 x 540)을 한 점씩 그리고 안 칠한 조사 칸(12 x 12 칸)은 어둡게 덮는다 — 한 장으로 둘 다 된다.
/// 「항구맵」(<c>0x00444250</c>)은 들른 항구(항구 표 +0x15 &amp; 0x40)에 점과 이름을 더한다.
/// </remarks>
public sealed class SeaChartWindow : Window
{
    private const int CellsPerSurvey = Survey.CellChips / ChipMap.PerCell;   // 12

    private SeaChartWindow(string title, WorldMap map, Survey survey, (double X, double Y)? fleet,
                           IReadOnlyList<(string Name, int X, int Y)> ports)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;

        int w = WorldMap.Width, h = WorldMap.Height;
        var bgra = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool seen = survey[x / CellsPerSurvey % Survey.Cols, Math.Min(y / CellsPerSurvey, Survey.Rows - 1)];
                byte v = map[x, y];
                uint c = v == WorldMap.Sea ? 0x17376Fu : v == WorldMap.Land ? 0xC8B88Au : 0x9A5A40u;
                if (!seen) c = (c >> 3) & 0x1F1F1Fu;
                bgra[y * w + x] = 0xFF000000u | c;
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var canvas = new Canvas { Width = w, Height = h, Margin = new Thickness(8) };
        canvas.Children.Add(new Image { Source = bmp, Width = w, Height = h });
        foreach (var (name, px, py) in ports)
        {
            double cx = px / (double)ChipMap.PerCell, cy = py / (double)ChipMap.PerCell;
            var dot = new Ellipse { Width = 5, Height = 5, Fill = Brushes.Gold };
            Canvas.SetLeft(dot, cx - 2.5); Canvas.SetTop(dot, cy - 2.5);
            canvas.Children.Add(dot);
            var label = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 10 };
            Canvas.SetLeft(label, cx + 4); Canvas.SetTop(label, cy - 7);
            canvas.Children.Add(label);
        }
        if (fleet is { } f)
        {
            double cx = f.X / ChipMap.PerCell, cy = f.Y / ChipMap.PerCell;
            var mark = new Ellipse { Width = 9, Height = 9, Stroke = Brushes.Red, StrokeThickness = 2 };
            Canvas.SetLeft(mark, cx - 4.5); Canvas.SetTop(mark, cy - 4.5);
            canvas.Children.Add(mark);
        }
        var stack = new StackPanel();
        stack.Children.Add(canvas);
        stack.Children.Add(new TextBlock
        {
            Text = $"조사한 칸 {survey.Total} / {Survey.Cols * Survey.Rows}",
            Foreground = Brushes.Gainsboro, Margin = new Thickness(8, 0, 8, 8),
        });
        Content = stack;
        KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Escape or System.Windows.Input.Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    public static void Show(Window owner, string title, WorldMap map, Survey survey, (double X, double Y)? fleet,
                            IReadOnlyList<(string Name, int X, int Y)> ports) =>
        new SeaChartWindow(title, map, survey, fleet, ports) { Owner = owner }.ShowDialog();
}
