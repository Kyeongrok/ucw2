using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 글 넣기 창 — 원본은 글자판(<c>0x0042DDE0</c> · <c>0x0042D0D0</c>)으로 받는다. 그냥 닫으면 null.
/// </summary>
public sealed class TextDialog : Window
{
    private readonly TextBox _box;
    private string? _value;

    private TextDialog(string title, string initial, int maxLength)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x10, 0x08));

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = $"{title} ({maxLength}자까지)",
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xE2, 0xCE)),
            Margin = new Thickness(0, 0, 0, 8),
        });
        _box = new TextBox { Text = initial, MinWidth = 200, MaxLength = maxLength };
        stack.Children.Add(_box);
        var ok = new Button { Content = "결정", Margin = new Thickness(0, 8, 0, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (_box.Text.Trim().Length > 0) _value = _box.Text.Trim();
            Close();
        };
        stack.Children.Add(ok);
        Content = stack;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    public static string? Ask(Window owner, string title, string initial, int maxLength)
    {
        var d = new TextDialog(title, initial, maxLength) { Owner = owner };
        d.ShowDialog();
        return d._value;
    }
}
