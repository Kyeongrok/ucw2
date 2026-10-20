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

    private BuildingMenu(string title, IReadOnlyList<string> items)
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
        Content = stack;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    /// <summary>띄우고 고른 줄의 차례를 돌려준다. 그냥 닫으면 -1.</summary>
    public static int Ask(Window owner, string title, IReadOnlyList<string> items)
    {
        var menu = new BuildingMenu(title, items) { Owner = owner };
        menu.ShowDialog();
        return menu._picked;
    }
}
