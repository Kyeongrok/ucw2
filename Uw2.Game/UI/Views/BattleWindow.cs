using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Uw2.Game.Engine.Sea;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 해전 화면 — 헥스 판과 명령(<c>0x00457B28</c>: 이동 · 공격 · 정보 · 신호 · 작전 · 정선 · ITEM · 이탈).
/// </summary>
/// <remarks>
/// 원본은 헥스를 한 칸씩 골라 길을 잇는다(<c>0x0040BD60</c>, 말 0x2FF · 0x300). 여기서는 갈 수 있는 칸(<see cref="Battle.Reach"/>)을
/// 칠해 두고 닿을 칸을 누르면 꺾음이 가장 적은 길로 간다. 다 가면 칠 적을 고른다(<c>0x0040C510</c>, 말 0x304).
/// 신호(제 기함 +0x16 의 비트만 뒤집고 읽는 데가 없다) · ITEM 은 아직 없다.
/// </remarks>
public sealed class BattleWindow : Window
{
    private const double CellW = 14, CellH = 12;

    private static readonly Brush SeaBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x3C, 0x6C));
    private static readonly Brush SeaBrush2 = new SolidColorBrush(Color.FromRgb(0x1C, 0x44, 0x78));
    private static readonly Brush LandBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x5C, 0x30));
    private static readonly Brush ReachBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x60, 0xC0, 0x60));
    private static readonly Brush TargetBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xE0, 0x40, 0x30));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xE2, 0xCE));

    private readonly Battle _b;
    private readonly GameMessages? _msg;
    private readonly Canvas _board = new() { Width = Battle.Width * CellW, Height = Battle.Height * CellH + CellH / 2, ClipToBounds = true };
    private readonly TextBlock _status = new() { Foreground = TextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox _log = new() { Height = 330, Background = Brushes.Black, Foreground = TextBrush, BorderThickness = new Thickness(0) };
    private readonly StackPanel _buttons = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(90) };

    private enum Pick { None, Move, Attack, Info, Order, OrderTarget }
    private Pick _pick;
    private List<Battle.Route> _routes = [];
    private (List<Battle.Ship> Guns, List<Battle.Ship> Board) _targets = ([], []);
    private Battle.Ship? _flag;   // 명령을 기다리는 제 기함

    private readonly Func<Battle.Ship, Duel.Fighter>? _gear;

    private BattleWindow(Battle b, GameMessages? msg, string title, Func<Battle.Ship, Duel.Fighter>? gear = null)
    {
        _b = b; _msg = msg; _gear = gear;
        b.Duels = gear != null;
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x10, 0x08));

        var side = new StackPanel { Width = 260, Margin = new Thickness(8) };
        side.Children.Add(_status);
        side.Children.Add(_buttons);
        side.Children.Add(_log);
        var root = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(side, Dock.Right);
        root.Children.Add(side);
        root.Children.Add(new Border { BorderBrush = Brushes.SaddleBrown, BorderThickness = new Thickness(2), Child = _board });
        Content = root;

        _board.MouseLeftButtonDown += OnBoardClick;
        _board.MouseRightButtonDown += (_, _) => Cancel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };
        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) => { Draw(); _timer.Start(); };
        Closing += (_, e) => { if (_b.Result == 0) e.Cancel = true; };
    }

    /// <summary>판을 끝까지 치른다 — 닫히면 <see cref="Battle.Result"/> 가 차 있다.</summary>
    public static void Run(Window? owner, Battle b, GameMessages? msg, string title, Func<Battle.Ship, Duel.Fighter>? gear = null)
    {
        var w = new BattleWindow(b, msg, title, gear) { Owner = owner };
        if (owner == null) w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
    }

    /// <summary>개발 — 게임 없이 판 하나를 띄운다(<c>Uw2Play.exe --battle-demo</c>). 뭍 칩 하나를 둔 바다에 네 척씩.</summary>
    public static void Demo()
    {
        var rng = new Random();
        var b = new Battle(n => n < 2 ? 0 : rng.Next(n)) { Time = 20 };
        b.BuildMap((cx, cy) => !(cx is 2 or 3 && cy == 2));
        b.RollWind();
        Battle.Captain Cap(int p) => new() { Person = p, Stats = [70, 70, 50, 50, 50, 60, 50, 50], NavLevel = 10, BattleLevel = 10 };
        string[] ours = ["산타 마리아", "핀타", "니나", "라 갈레가"];
        for (int i = 0; i < 4; i++)
            b.Add(0, Cap(0), ours[i], 120, 80, 80, 60, 60, 4, 24, 60, 50, true, true, false, 80, 120);
        for (int i = 0; i < 4; i++)
            b.Add(1, Cap(1), $"해적선 {i + 1}", 100, 70, 70, 55, 55, 1 + i % 3, 20, 40, 50, false, i % 2 == 0, false, 70, 100);
        b.Deploy(0, 2, -2, 0);
        b.Deploy(1, 6, 0, 0);
        var w = new BattleWindow(b, null, "해전 — 연습") { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        if (Environment.GetEnvironmentVariable("UW2_SHOT") is { Length: > 0 } shot)
        {
            // 개발 — 몇 초 뒤 창을 그림으로 떠 둔다.
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (w.Content is not FrameworkElement root) return;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(root);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var fs = System.IO.File.Create(shot)) enc.Save(fs);
            };
            w.Loaded += (_, _) => t.Start();
        }
        w.ShowDialog();
    }

    private string Msg(int id, string fallback, params object[] args) =>
        _msg is { } m && m[id].Length > 0 ? m.Format(id, args).Replace("\\n", "\n") : fallback;

    // ── 틱 ─────────────────────────────────────────────────────────────────

    /// <summary>남 배 하나씩 움직인다 — 제 기함 차례가 오면 멈추고 명령을 기다린다.</summary>
    private void Advance()
    {
        if (_flag != null) return;
        if (_b.Result != 0) { Finish(); return; }
        var s = _b.NextShip();
        if (s == null) { Finish(); return; }
        if (s == _b.PlayerFlagship)
        {
            _flag = s;
            ShowCommands();
            Draw();
            return;
        }
        foreach (var h in _b.AiTurn(s)) Log(h);
        if (s.Left) Log($"{Who(s)} 이(가) 싸움에서 빠졌다.");
        if (_b.DuelRequested is var (by, to))
        {
            // 남 기함이 걸었다(0x00412B98) — 원본 글은 일본어판 그대로 남은 「敵の提督が一騎討ちを仕掛けてきました」(0x00457E68)다.
            _timer.Stop();
            Duel(by, to, "적의 제독이 일기토를 걸어 왔습니다！");
            _timer.Start();
        }
        Draw();
    }

    private void EndTurn()
    {
        _flag = null;
        _pick = Pick.None;
        _buttons.Children.Clear();
        _b.CheckEnd();
        Draw();
    }

    private void Finish()
    {
        _timer.Stop();
        int id = _b.Result switch
        {
            Battle.Outcome.Sunset => 0x233,
            Battle.Outcome.PlayerLeft => 0x234,
            Battle.Outcome.EnemyLeft => 0x235,
            Battle.Outcome.Surrendered => 0x236,
            _ => -1,
        };
        string text = id >= 0 ? Msg(id, "싸움이 끝났다.")
            : _b.Result is Battle.Outcome.EnemySunk or Battle.Outcome.DuelWon ? "적 기함을 쓰러뜨렸다！"
            : "기함을 잃었다……";
        MessageBox.Show(this, text, Title);
        Close();
    }

    // ── 명령 ───────────────────────────────────────────────────────────────

    private void ShowCommands()
    {
        _buttons.Children.Clear();
        var f = _flag!;
        var (guns, board) = _b.Targets(f);
        Add("이동", () => BeginMove());
        Add("공격", () => BeginAttack(), guns.Count + board.Count > 0);
        Add("정보", () => { _pick = Pick.Info; Say("볼 배를 고르세요."); });
        Add("작전", () => { _pick = Pick.Order; Say(Msg(0x305, "작전을 전달할 배를 선택해 주세요.")); },
            _b.Friends(f).Any(s => s != f && s.Alive));
        Add("정선", () =>
        {
            if (MessageBox.Show(this, Msg(0x307, "아무 것도 안해도 좋습니까？"), Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes) EndTurn();
        });
        Add("이탈", () =>
        {
            if (MessageBox.Show(this, Msg(0x308, "정말로 이탈하겠습니까？"), Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _b.Leave(f);
            Log($"{Who(f)} 이(가) 싸움에서 빠졌다.");
            EndTurn();
        }, _b.AtEdge(f.X, f.Y));
    }

    private void Add(string text, Action click, bool enabled = true)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 0, 4), IsEnabled = enabled, Padding = new Thickness(4) };
        b.Click += (_, _) => click();
        _buttons.Children.Add(b);
    }

    private void BeginMove()
    {
        _routes = _b.Reach(_flag!);
        _pick = Pick.Move;
        Say(Msg(0x2FF, "이동할 ＨＥＸ를 선택해 주세요.") + "\n(앞의 옆 칸을 누르면 제자리에서 돈다)");
        Draw();
    }

    private void BeginAttack()
    {
        _targets = _b.Targets(_flag!);
        if (_targets.Guns.Count + _targets.Board.Count == 0) { EndTurn(); return; }
        _pick = Pick.Attack;
        Say(Msg(0x304, "공격할 적을 선택해 주세요."));
        Draw();
    }

    private void Cancel()
    {
        if (_flag == null || _pick == Pick.None) return;
        bool afterMove = _pick == Pick.Attack && _moved;
        _pick = Pick.None;
        if (afterMove) { _moved = false; EndTurn(); return; }
        ShowCommands();
        Draw();
    }

    private bool _moved;
    private Battle.Ship? _ordered;

    private void OnBoardClick(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_board);
        int x = (int)(p.X / CellW);
        int y = (int)((p.Y - (x & 1) * CellH / 2) / CellH);
        if (!Battle.InBounds(x, y)) return;
        var f = _flag;
        switch (_pick)
        {
            case Pick.Info:
                if (_b.ShipAt(x, y) is { } info) MessageBox.Show(this, Describe(info), Title);
                _pick = Pick.None;
                break;
            case Pick.Order when f != null:
            {
                if (_b.ShipAt(x, y) is not { } ally || ally == f || !_b.Friends(f).Contains(ally)) break;
                _ordered = ally;
                while (true)
                {
                    string[] items =
                    [
                        Battle.Offensive(ally) ? Msg(0x401, "공세").Trim() : Msg(0x402, "수세").Trim(),
                        ally.Gunner ? Msg(0x405, "포격").Trim() : Msg(0x406, "돌격").Trim(),
                        ally.Target is { } tg ? $"목표 {tg.Name}" : "목표 없음",
                    ];
                    int k = BuildingMenu.Ask(this, $"{ally.Name} 작전", items);
                    if (k < 0) break;
                    if (k == 2 && ally.Target == null)
                    {
                        _pick = Pick.OrderTarget;
                        Say(Msg(0x306, "공격상대를 선택해 주세요."));
                        Draw();
                        return;
                    }
                    Battle.Order(ally, k);
                }
                _pick = Pick.None;
                ShowCommands();
                break;
            }
            case Pick.OrderTarget when f != null && _ordered != null:
            {
                if (_b.ShipAt(x, y) is not { } enemy || !_b.Enemies(f).Contains(enemy)) break;
                Battle.Order(_ordered, 2, enemy);
                Log($"{_ordered.Name} → 목표 {Who(enemy)}");
                _pick = Pick.None;
                ShowCommands();
                break;
            }
            case Pick.Move when f != null:
            {
                var route = _routes.Where(r => r.X == x && r.Y == y && r.Dirs.Count > 0).OrderBy(r => r.Turns).FirstOrDefault();
                if (route != null)
                {
                    _b.Move(f, route.Dirs);
                    _moved = true;
                    Draw();
                    var t = _b.Targets(f);
                    if (t.Guns.Count + t.Board.Count > 0) { BeginAttack(); return; }
                    _moved = false;
                    EndTurn();
                    return;
                }
                // 앞 옆 칸 — 제자리에서 돌기(0x0040C3A0, 말 0x301).
                foreach (int k in (int[])[-1, 1])
                {
                    int dir = (f.Heading + k + 6) % 6;
                    if (Battle.Step(f.X, f.Y, dir) == (x, y))
                    {
                        _b.Turn(f, dir);
                        EndTurn();
                        return;
                    }
                }
                break;
            }
            case Pick.Attack when f != null:
            {
                var t = _b.ShipAt(x, y);
                if (t == null) break;
                // 적 기함에 붙었으면 일기토를 묻는다(0x0040C5E0, 말 0x302) — 받아들이지 않으면(0x303) 백병전이다.
                if (_gear != null && _targets.Board.Contains(t) && !_targets.Guns.Contains(t) && t.Slot == 0 && f.Slot == 0
                    && MessageBox.Show(this, Msg(0x302, "적의 제독과 일기토를 하겠습니까？"), Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    if (_b.DuelAccepted(f, t))
                    {
                        Duel(f, t, "일기토！");
                        _moved = false;
                        EndTurn();
                        return;
                    }
                    MessageBox.Show(this, Msg(0x303, "적의 방해를 받았습니다."), Title);
                }
                Battle.Hit? hit = _targets.Guns.Contains(t) ? _b.Fire(f, t) : _targets.Board.Contains(t) ? _b.Board(f, t) : null;
                if (hit is not { } h) break;
                Log(h);
                _moved = false;
                EndTurn();
                return;
            }
        }
        Draw();
    }

    /// <summary>일기토를 치르고 판에 돌려준다.</summary>
    private void Duel(Battle.Ship challenger, Battle.Ship defender, string intro)
    {
        var duel = _b.OpenDuel(challenger, defender, _gear!(challenger), _gear!(defender));
        DuelWindow.Run(this, duel, challenger.Player ? 0 : 1, intro);
        _b.DuelOver(duel, challenger, defender);
        Log(duel.Rope is 0 or Engine.Sea.Duel.Full ? $"일기토 — {(_b.Result == Battle.Outcome.DuelWon ? "적 제독을 쓰러뜨렸다！" : "제독이 쓰러졌다……")}"
                                                    : "일기토는 승부를 가리지 못했다.");
        Draw();
    }

    // ── 글 ─────────────────────────────────────────────────────────────────

    private void Say(string text) => _status.Text = Header() + "\n" + text;

    private string Header()
    {
        string[] dirs = ["북", "북동", "남동", "남", "남서", "북서"];
        return $"시각 {_b.Time / 3}시 · 바람 {dirs[_b.WindDir]} {_b.WindPower}";
    }

    private string Who(Battle.Ship s) => s.Player ? $"우리 {s.Name}" : $"적 {s.Name}";

    private void Log(Battle.Hit h)
    {
        string what = h.Cannon ? $"포격 — 내구 −{h.Hull} · 선원 −{h.Crew}" : $"백병전 — 적 선원 −{h.Crew} · 우리 선원 −{h.Damage}";
        Log($"{Who(h.By)} → {Who(h.To)}: {what}");
        if (h.Sunk) Log($"{Who(h.To)} 이(가) 가라앉았다！");
        else if (h.Taken) Log($"{Who(h.To)} 의 선원이 다 쓰러졌다.");
    }

    private void Log(string line)
    {
        _log.Items.Add(line);
        _log.ScrollIntoView(line);
    }

    private static string Describe(Battle.Ship s) =>
        $"{s.Name}\n선원 {s.Crew} · 내구 {s.Hull}/{s.MaxHull}\n돛 {s.Sail} · 역풍 {s.Upwind}\n" +
        $"대포 {s.Guns}문(사거리 {s.Range}) · 탄약 {s.Shot}\n백병 세기 {s.Melee} · 갑판 {s.DeckShare}%";

    // ── 그리기 ─────────────────────────────────────────────────────────────

    private void Draw()
    {
        _board.Children.Clear();
        var reach = _pick == Pick.Move ? _routes.Select(r => (r.X, r.Y)).ToHashSet() : [];
        for (int y = 0; y < Battle.Height; y++)
            for (int x = 0; x < Battle.Width; x++)
            {
                byte c = _b.CellAt(x, y);
                Brush fill = (c & Battle.Land) != 0 ? LandBrush : (c & 1) != 0 ? SeaBrush : SeaBrush2;
                if (reach.Contains((x, y))) fill = ReachBrush;
                var r = new Rectangle { Width = CellW - 1, Height = CellH - 1, Fill = fill };
                Canvas.SetLeft(r, x * CellW);
                Canvas.SetTop(r, y * CellH + (x & 1) * CellH / 2);
                _board.Children.Add(r);
            }
        var targets = _pick == Pick.Attack ? _targets.Guns.Concat(_targets.Board).ToHashSet() : [];
        foreach (var s in _b.All)
        {
            if (!s.Alive && !s.Wreck) continue;
            double cx = s.X * CellW + CellW / 2, cy = s.Y * CellH + (s.X & 1) * CellH / 2 + CellH / 2;
            Brush body = s.Wreck ? Brushes.Gray : s.Player ? Brushes.Gold : (s.Side & 1) == (_b.PlayerFlagship?.Side ?? 0) % 2 ? Brushes.LightSkyBlue : Brushes.IndianRed;
            var dot = new Ellipse
            {
                Width = CellW - 2, Height = CellH - 2, Fill = body,
                Stroke = targets.Contains(s) ? Brushes.White : s == _flag ? Brushes.Yellow : Brushes.Black,
                StrokeThickness = targets.Contains(s) || s == _flag ? 2 : 1,
            };
            Canvas.SetLeft(dot, cx - dot.Width / 2);
            Canvas.SetTop(dot, cy - dot.Height / 2);
            _board.Children.Add(dot);
            var (nx, ny) = Battle.Step(s.X, s.Y, s.Heading);
            double hx = nx * CellW + CellW / 2, hy = ny * CellH + (nx & 1) * CellH / 2 + CellH / 2;
            _board.Children.Add(new Line { X1 = cx, Y1 = cy, X2 = cx + (hx - cx) * 0.7, Y2 = cy + (hy - cy) * 0.7, Stroke = Brushes.White, StrokeThickness = 2 });
            if (s.Slot == 0)
                _board.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = Brushes.White, Margin = new Thickness(cx - 2, cy - 2, 0, 0) });
        }
        if (_flag == null || _pick == Pick.None)
        {
            var f = _flag ?? _b.PlayerFlagship;
            _status.Text = Header() + (f != null ? $"\n기함 {f.Name} — 선원 {f.Crew} · 내구 {f.Hull}/{f.MaxHull} · 탄약 {f.Shot}" : "")
                           + (_flag != null ? "\n명령을 고르세요." : "");
        }
    }
}
