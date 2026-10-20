using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 수 넣기 창 — 원본 <c>0x0042CF00(아래, 위)</c> 자리. 아래 · 위 사이의 수를 받는다(그냥 닫으면 -1).
/// </summary>
public sealed class AmountDialog : Window
{
    private readonly TextBox _box;
    private readonly int _max;
    private int _value = -1;

    private AmountDialog(string title, int max)
    {
        _max = max;
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x10, 0x08));

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = $"{title} (0 ~ {max})",
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xE2, 0xCE)),
            Margin = new Thickness(0, 0, 0, 8),
        });
        _box = new TextBox { Text = max.ToString(), MinWidth = 160 };
        stack.Children.Add(_box);
        var ok = new Button { Content = "결정", Margin = new Thickness(0, 8, 0, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (int.TryParse(_box.Text, out int v)) _value = Math.Clamp(v, 0, _max);
            Close();
        };
        stack.Children.Add(ok);
        Content = stack;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    public static int Ask(Window owner, string title, int max)
    {
        if (max <= 0) return -1;
        var d = new AmountDialog(title, max) { Owner = owner };
        d.ShowDialog();
        return d._value;
    }
}
