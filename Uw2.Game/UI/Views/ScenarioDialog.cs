using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Uw2.Game.Engine;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 시나리오 대사 창 — 한 줄씩 넘긴다(원본 <c>C8</c> 대사 · <c>C7</c> 기다림 자리). 누르거나 Enter · Space 로 다음.
/// </summary>
public sealed class ScenarioDialog : Window
{
    private readonly IReadOnlyList<ScenarioVm.Line> _lines;
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
    private int _at;
    private string _lastSpeaker = "";

    private ScenarioDialog(IReadOnlyList<ScenarioVm.Line> lines)
    {
        _lines = lines;
        Title = "대항해시대2";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x0C, 0x06));
        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(_speaker);
        stack.Children.Add(_body);
        stack.Children.Add(_page);
        Content = stack;
        MouseLeftButtonUp += (_, _) => Next();
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) Next();
            else if (e.Key == Key.Escape) Close();
        };
        Show(0);
    }

    private void Show(int i)
    {
        var line = _lines[i];
        // 말하는 사람이 안 적힌 줄은 앞사람이 이어 말하는 것이다.
        if (line.Speaker.Length > 0) _lastSpeaker = line.Speaker;
        _speaker.Text = _lastSpeaker;
        _body.Text = line.Body;
        _page.Text = $"{i + 1} / {_lines.Count}";
    }

    private void Next()
    {
        if (++_at >= _lines.Count) { Close(); return; }
        Show(_at);
    }

    public static void Play(Window owner, IReadOnlyList<ScenarioVm.Line> lines)
    {
        if (lines.Count == 0) return;
        new ScenarioDialog(lines) { Owner = owner }.ShowDialog();
    }
}
