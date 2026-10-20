using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 건물에 들었을 때 뜨는 고르기 창. 줄은 <c>MENU.DAT</c> 의 그 건물 목록 그대로다.
/// </summary>
/// <remarks>
/// <c>MENU.DAT</c> 는 목록마다 <c>06 개수</c> 머리를 달고 글을 0 으로 끊어 늘어놓는다 —
/// 항구는 <c>06 03</c> 「출  항 · 보  급 · 명  명」, 그 뒤 <c>06 0C</c> 가 건물 이름 열둘이다(<c>0x9AD</c> 언저리).
/// 항구 · 교역소가 있고, 고른 줄의 차례를 돌려준다(닫으면 -1).
/// </remarks>
public sealed class BuildingMenu : Window
{
    /// <summary>항구 목록 — <c>MENU.DAT</c> 그대로.</summary>
    public static readonly string[] Harbor = ["출  항", "보  급", "명  명"];

    /// <summary>교역소 목록 — <c>MENU.DAT 0xFF</c> 「구  입 · 매  각」.</summary>
    public static readonly string[] Trade = ["구  입", "매  각"];

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
