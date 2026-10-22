using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 「항구맵」(<c>0x00444250</c>) — 지금 항구를 줄여 그리고 건물 문마다 이름을, 사람 자리에 표를 단다.
/// 원본은 항구를 4 x 4 판(칸 24 씩)으로 나눠 사람이 선 판을 비추는데, 우리는 한 장에 다 보인다.
/// </summary>
public sealed class PortMapWindow : Window
{
    private PortMapWindow(string title, uint[] bgra, int size, int scale, (int X, int Y)?[] doors, (double X, double Y)? me)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;

        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, bgra, size * 4);
        bmp.Freeze();
        var canvas = new Canvas { Width = size, Height = size, Margin = new Thickness(8) };
        canvas.Children.Add(new Image { Source = bmp, Width = size, Height = size });
        for (int b = 0; b < doors.Length && b < PortDoors.Names.Length; b++)
        {
            if (doors[b] is not { } d) continue;
            double cx = (d.X + 0.5) * scale, cy = (d.Y + 0.5) * scale;
            var dot = new Rectangle { Width = 5, Height = 5, Fill = Brushes.Gold };
            Canvas.SetLeft(dot, cx - 2.5); Canvas.SetTop(dot, cy - 2.5);
            canvas.Children.Add(dot);
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                Child = new TextBlock { Text = PortDoors.Names[b].Replace(" ", ""), Foreground = Brushes.White, FontSize = 10 },
            };
            Canvas.SetLeft(label, cx + 4); Canvas.SetTop(label, cy - 7);
            canvas.Children.Add(label);
        }
        if (me is { } m)
        {
            double cx = (m.X + 0.5) * scale, cy = (m.Y + 0.5) * scale;
            var mark = new Ellipse { Width = 9, Height = 9, Stroke = Brushes.Red, StrokeThickness = 2 };
            Canvas.SetLeft(mark, cx - 4.5); Canvas.SetTop(mark, cy - 4.5);
            canvas.Children.Add(mark);
        }
        Content = canvas;
        KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Escape or System.Windows.Input.Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    public static void Show(Window owner, string title, uint[] bgra, int size, int scale, (int X, int Y)?[] doors, (double X, double Y)? me) =>
        new PortMapWindow(title, bgra, size, scale, doors, me) { Owner = owner }.ShowDialog();
}
