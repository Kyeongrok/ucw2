using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Uw2.Game.Engine.Sea;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 일기토 화면(<c>0x004130E0</c>) — 차례마다 공격(찌르기 · 베기 · 강타) 또는 방어(쳐내기 · 받기 · 피하기)를 고른다.
/// 원본은 칼 휘두르는 그림을 띄운다. 여기서는 줄과 글로 보인다.
/// </summary>
public sealed class DuelWindow : Window
{
    private static readonly string[] Attacks = ["찌르기", "베기", "강타"], Defenses = ["쳐내기", "받기", "피하기"];
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xE2, 0xCE));

    private readonly Duel _duel;
    private readonly int _me;   // 주인공 쪽(0 건 쪽 · 1 받은 쪽)
    private readonly TextBlock _turn = new() { Foreground = TextBrush, FontSize = 16, Margin = new Thickness(0, 0, 0, 6) };
    private readonly ProgressBar _rope = new() { Minimum = 0, Maximum = Duel.Full, Height = 18, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _log = new() { Foreground = TextBrush, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

    private DuelWindow(Duel duel, int me, string intro)
    {
        _duel = duel; _me = me;
        Title = "일기토";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x10, 0x08));
        var root = new StackPanel { Margin = new Thickness(16), Width = 380 };
        var names = new DockPanel();
        var left = new TextBlock { Text = duel.Sides[0].Name, Foreground = TextBrush };
        var right = new TextBlock { Text = duel.Sides[1].Name, Foreground = TextBrush, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(left, Dock.Left);
        names.Children.Add(left);
        names.Children.Add(right);
        root.Children.Add(_turn);
        root.Children.Add(names);
        // 줄 — 200 쪽이 건 쪽. 막대가 왼쪽(건 쪽)으로 찰수록 건 쪽이 이긴다.
        _rope.FlowDirection = FlowDirection.RightToLeft;
        root.Children.Add(_rope);
        root.Children.Add(_log);
        root.Children.Add(_buttons);
        Content = root;
        _log.Text = intro;
        Closing += (_, e) => { if (!_duel.Over) e.Cancel = true; };
        Refresh();
    }

    /// <summary>일기토를 끝까지 치른다(<paramref name="me"/> 는 주인공 쪽).</summary>
    public static void Run(Window owner, Duel duel, int me, string intro)
    {
        var w = new DuelWindow(duel, me, intro) { Owner = owner };
        w.ShowDialog();
    }

    private void Refresh()
    {
        _rope.Value = _duel.Rope;
        _buttons.Children.Clear();
        if (_duel.Over)
        {
            string end = _duel.Rope == Duel.Full ? $"{_duel.Sides[0].Name} 의 승리！"
                : _duel.Rope == 0 ? $"{_duel.Sides[1].Name} 의 승리！" : "승부를 가리지 못했다.";
            _turn.Text = end;
            var ok = new Button { Content = "확인", Padding = new Thickness(12, 4, 12, 4) };
            ok.Click += (_, _) => Close();
            _buttons.Children.Add(ok);
            return;
        }
        bool attacking = _duel.Attacker == _me;
        _turn.Text = $"제 {_duel.Turn / 2 + 1} 차례 : {(attacking ? "공  격" : "방  어")}";
        var names = attacking ? Attacks : Defenses;
        for (int k = 0; k < 3; k++)
        {
            int pick = k;
            var b = new Button { Content = names[k], Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (_, _) => Play(pick);
            _buttons.Children.Add(b);
        }
    }

    private void Play(int mine)
    {
        int other = _me ^ 1;
        int theirs = _duel.AiChoice(other);
        bool attacking = _duel.Attacker == _me;
        int dmg = attacking ? _duel.Exchange(mine, theirs) : _duel.Exchange(theirs, mine);
        var at = _duel.Sides[attacking ? _me : other];
        string move = attacking ? $"{Attacks[mine]} ↔ {Defenses[theirs]}" : $"{Attacks[theirs]} ↔ {Defenses[mine]}";
        _log.Text = $"{at.Name}: {move} — " + (dmg == 0 ? "빗나갔다！" : $"{dmg} 의 타격");
        Refresh();
    }
}
