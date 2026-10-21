using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Uw2.Game.Engine.Sea;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 일기토 화면(<c>0x004130E0</c>) — 차례마다 공격(찌르기 · 베기 · 강타) 또는 방어(쳐내기 · 받기 · 피하기)를 고른다.
/// 둘이 마주 서서(상대 왼쪽 · 주인공 오른쪽) 고른 수의 장을 함께 넘긴다(<see cref="Uw2.Support.Local.Formats.DuelSprites"/>) —
/// 공격은 무리 1 + 수, 방어는 4 + 수, 맞으면 무리 0 의 장 2, 끝나면 이긴 쪽이 장 4 · 5, 진 쪽이 장 3.
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

    /// <summary>일기토 한 장 — (주인공 쪽인가, 무기 갈래, 무리, 장). 붙이는 쪽이 채운다. 없으면 그림 없이 줄과 글만.</summary>
    public static Func<bool, int, int, int, ImageSource?>? Sprites { get; set; }

    /// <summary>일기토 마당 그림(GRAPH 24, 480 x 176 — 원본은 화면 23 위 (80, 168) 에 깐다, <c>0x0041462B</c>).</summary>
    public static ImageSource? Arena { get; set; }

    private const double SpriteScale = 1.5;
    private readonly Image[] _figure = [new(), new()];      // 쪽마다(0 건 쪽 · 1 받은 쪽)
    private readonly System.Windows.Threading.DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Queue<(int Group0, int Frame0, int Group1, int Frame1)> _frames = new();

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
        if (Sprites != null)
        {
            // 두 판은 같은 무대(176 x 120)의 자리를 쓴다 — 상대가 왼쪽(오른쪽을 본다), 주인공이 오른쪽(왼쪽을 본다). 겹쳐 놓는다.
            var stage = new Grid { HorizontalAlignment = HorizontalAlignment.Center,
                                   Background = new SolidColorBrush(Color.FromRgb(0x10, 0x18, 0x30)), Margin = new Thickness(0, 0, 0, 6) };
            double stageW = Uw2.Support.Local.Formats.DuelSprites.CellW * SpriteScale;
            if (Arena is { } arena)
            {
                var floor = new Image { Source = arena, Width = arena.Width * SpriteScale, Height = arena.Height * SpriteScale };
                RenderOptions.SetBitmapScalingMode(floor, BitmapScalingMode.NearestNeighbor);
                stage.Children.Add(floor);
                stageW = floor.Width;
            }
            foreach (var fig in _figure)
            {
                fig.Width = Uw2.Support.Local.Formats.DuelSprites.CellW * SpriteScale;
                fig.Height = Uw2.Support.Local.Formats.DuelSprites.CellH * SpriteScale;
                fig.VerticalAlignment = VerticalAlignment.Bottom;
                fig.Margin = new Thickness(0, 0, 0, 8 * SpriteScale);
                RenderOptions.SetBitmapScalingMode(fig, BitmapScalingMode.NearestNeighbor);
                stage.Children.Add(fig);
            }
            root.Width = stageW;
            root.Children.Add(stage);
            Pose(0, 0, 0, 0);
            _clock.Tick += (_, _) => Step();
        }
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
        int attackerSide = _duel.Attacker;
        int attack = attacking ? mine : theirs, defend = attacking ? theirs : mine;
        int dmg = attacking ? _duel.Exchange(mine, theirs) : _duel.Exchange(theirs, mine);
        var at = _duel.Sides[attacking ? _me : other];
        string move = attacking ? $"{Attacks[mine]} ↔ {Defenses[theirs]}" : $"{Attacks[theirs]} ↔ {Defenses[mine]}";
        _log.Text = $"{at.Name}: {move} — " + (dmg == 0 ? "빗나갔다！" : $"{dmg} 의 타격");
        if (Sprites == null) { Refresh(); return; }
        Animate(attackerSide, attack, defend, dmg > 0);
    }

    /// <summary>한 수를 장으로 넘긴다 — 공격 무리 1 + 수 · 방어 무리 4 + 수를 함께, 맞았으면 맞은 장, 그 뒤 서기(또는 끝 자세).</summary>
    private void Animate(int attackerSide, int attack, int defend, bool hit)
    {
        var frames = Uw2.Support.Local.Formats.DuelSprites.Frames;
        int ag = 1 + attack, dg = 4 + defend, n = Math.Max(frames[ag], frames[dg]);
        (int, int)[] pose = new (int, int)[2];
        for (int i = 0; i < n; i++)
        {
            pose[attackerSide] = (ag, Math.Min(i, frames[ag] - 1));
            pose[attackerSide ^ 1] = (dg, Math.Min(i, frames[dg] - 1));
            _frames.Enqueue((pose[0].Item1, pose[0].Item2, pose[1].Item1, pose[1].Item2));
        }
        if (hit)
        {
            pose[attackerSide] = (ag, frames[ag] - 1);
            pose[attackerSide ^ 1] = (0, 2);
            _frames.Enqueue((pose[0].Item1, pose[0].Item2, pose[1].Item1, pose[1].Item2));
            _frames.Enqueue((pose[0].Item1, pose[0].Item2, pose[1].Item1, pose[1].Item2));
        }
        if (_duel.Over && _duel.Rope is 0 or Duel.Full)
        {
            int winner = _duel.Rope == Duel.Full ? 0 : 1;
            for (int i = 0; i < 6; i++)
            {
                pose[winner] = (0, 4 + i % 2);
                pose[winner ^ 1] = (0, 3);
                _frames.Enqueue((pose[0].Item1, pose[0].Item2, pose[1].Item1, pose[1].Item2));
            }
        }
        else _frames.Enqueue((0, 0, 0, 0));
        _buttons.IsEnabled = false;
        _clock.Start();
    }

    private void Step()
    {
        if (_frames.Count == 0)
        {
            _clock.Stop();
            _buttons.IsEnabled = true;
            Refresh();
            return;
        }
        var (g0, f0, g1, f1) = _frames.Dequeue();
        Pose(g0, f0, g1, f1);
    }

    private void Pose(int g0, int f0, int g1, int f1)
    {
        if (Sprites is not { } art) return;
        _figure[0].Source = art(_me == 0, Variant(0), g0, f0);
        _figure[1].Source = art(_me == 1, Variant(1), g1, f1);
    }

    /// <summary>무기 겹그림 — 채비의 무기 갈래(1 ~ 3, 없으면 0).</summary>
    private int Variant(int side) => Math.Clamp(_duel.Sides[side].Style, 0, Uw2.Support.Local.Formats.DuelSprites.Variants - 1);

    /// <summary>개발 — 화면 밖에 띄워 한 수를 넘긴 뒤 그림으로 적는다(<c>Uw2Play.exe --duel-shot 파일</c>).</summary>
    public static void DevShot(string path)
    {
        var duel = new Duel(new Duel.Fighter("조안", 60, 10, 1, 20, 0), new Duel.Fighter("해적", 50, 8, 2, 10, 0), 100, Random.Shared.Next);
        var w = new DuelWindow(duel, 0, "일기토！") { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0 };
        w.Show();
        w.Pose(1, 1, 4, 1);
        w.UpdateLayout();
        var root = (FrameworkElement)w.Content;
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = System.IO.File.Create(path)) enc.Save(fs);
        w.Hide();   // 끝나지 않은 일기토는 닫히지 않는다 — 감추고 껍데기가 끝낸다
    }
}
