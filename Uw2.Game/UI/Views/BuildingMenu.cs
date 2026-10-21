using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 건물에 들었을 때 뜨는 고르기 창. 줄은 <c>MENU.DAT</c> 의 그 건물 목록 그대로다.
/// </summary>
/// <remarks>
/// 목록은 <see cref="Uw2.Support.Local.Formats.MenuTable"/> 이 <c>MENU.DAT</c> 에서 읽는다.
/// 고른 줄의 차례를 돌려준다(닫으면 -1).
/// </remarks>
public sealed class BuildingMenu : Window
{
    private int _picked = -1;

    private BuildingMenu(string title, IReadOnlyList<string> items, ImageSource? picture = null)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x10, 0x08));

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xE2, 0xCE)),
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 10),
        });
        for (int i = 0; i < items.Count; i++)
        {
            int at = i;
            var b = new Button
            {
                Content = items[i],
                MinWidth = 160,
                Margin = new Thickness(0, 3, 0, 3),
                Padding = new Thickness(8, 4, 8, 4),
            };
            b.Click += (_, _) => { _picked = at; Close(); };
            stack.Children.Add(b);
        }
        if (picture is null) Content = stack;
        else
        {
            // 건물 임자 그림 — 원본 크기 그대로 두 배로, 점이 뭉개지지 않게 키운다.
            var img = new Image { Source = picture, Width = picture.Width * 2, Height = picture.Height * 2, Margin = new Thickness(16, 16, 0, 16), VerticalAlignment = VerticalAlignment.Top };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(img);
            row.Children.Add(stack);
            Content = row;
        }
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    /// <summary>띄우고 고른 줄의 차례를 돌려준다. 그냥 닫으면 -1.</summary>
    public static int Ask(Window owner, string title, IReadOnlyList<string> items, ImageSource? picture = null)
    {
        var menu = new BuildingMenu(title, items, picture) { Owner = owner };
        menu.ShowDialog();
        return menu._picked;
    }
}
