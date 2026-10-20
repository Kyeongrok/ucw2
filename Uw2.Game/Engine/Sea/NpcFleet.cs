using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 남의 함대 하나 — 뱃길 그래프를 따라 목표 항구로 간다(<c>KOUKAI2.EXE 0x00418250</c> 의 한 바퀴).
/// </summary>
/// <remarks>
/// 틱마다 모든 남 함대가 한 걸음씩 간다. 빠르기는 <c>0x00418020</c> — 제 함대 셈과 효율표는 같되
/// 선원 · 짐을 안 보고 <b>제독 하나</b>의 항해 레벨 · 항해술을 쓴다. 바람 · 해류는 <b>제 함대 자리의 것</b>을
/// 모두가 같이 쓴다(<c>0x0049F5A0</c> · <c>0x0049F59C</c> 를 그대로 읽는다). 해류는 걸음에 안 더하고 빠르기에 더하고 뺀다.
/// <code>
///   해류 각 3~5   빠르기 -= min(빠르기 - 1, 해류세기)
///   해류 각 0·1·7 빠르기 += min(40 - 빠르기, 해류세기)
/// </code>
/// 뱃머리는 다음 마디(마지막엔 목표 자리)를 보고 정한다(<c>0x00448380</c>). 게임은 가로 · 세로 차를 쌓임 값으로
/// 비스듬히 나눠 가는데, 우리는 <b>긴 축이 짧은 축의 두 배를 넘으면 곧게, 아니면 대각선</b>으로 어림한다.
///
/// 닿은 뒤의 일(<c>0x004184B0</c>)은 움직임 갈래(+0x1B)마다 다르다. 0 갈래는 항구 번호 <c>+0x1C</c>(고향)로
/// 목표를 바꾸고 <c>rand(8) + 8</c> 을 +0x27 에 둔다. <b>우리는 처음 목표 항구와 고향을 오가며, 닿으면 그만큼의 날을
/// 항구에 머문다</b> — 새 목표를 고르는 손은 아직 안 옮겼다.
/// </remarks>
public sealed class NpcFleet
{
    private readonly Fleet _sea;
    private readonly RouteGraph _graph;
    private readonly Random _rng;
    private readonly ShipSailing[] _ships;
    private readonly int _navLevel, _seamanship;
    private readonly (int X, int Y) _home, _away;

    private int _cellX, _cellY, _subX = Sailing.CellUnits / 2, _subY = Sailing.CellUnits / 2;
    private readonly List<(int X, int Y)> _route = [];
    private int _leg;
    private bool _towardHome;

    public NpcFleet(Fleet sea, RouteGraph graph, Random rng, StartData.Fleet record, StartData start,
                    (int X, int Y) home, (int X, int Y) target)
    {
        _sea = sea; _graph = graph; _rng = rng;
        Index = record.Index;
        _cellX = record.X; _cellY = record.Y;
        Heading = record.Heading & 7;
        var admiral = start.People[Math.Clamp(record.Admiral, 0, start.People.Count - 1)];
        _navLevel = admiral.NavLevel; _seamanship = admiral.Seamanship;
        _ships = [.. record.Slots.Where(s => s.Present).Select(s =>
        {
            var t = start.ShipTypes[start.GuessType(s)];
            return new ShipSailing(t.SailKind, s.Sail, s.Upwind, false, 0, 0, 0, 0, 0, 0, 0);
        })];
        _home = home; _away = target;
        Plan(target);
    }

    public int Index { get; }
    public int Heading { get; private set; }
    public double X => _cellX + _subX / (double)Sailing.CellUnits;
    public double Y => _cellY + _subY / (double)Sailing.CellUnits;

    /// <summary>항구에 머무는 남은 날. 0 이 아니면 지도에 안 그린다.</summary>
    public int DaysInPort { get; private set; }

    /// <summary>항구에 들어 있는지.</summary>
    public bool Docked => DaysInPort > 0;

    private void Plan((int X, int Y) target)
    {
        _route.Clear();
        _leg = 0;
        var nodes = _graph.Path(_graph.Nearest(_cellX, _cellY), _graph.Nearest(target.X, target.Y));
        foreach (int n in nodes) _route.Add((_graph.Nodes[n].X, _graph.Nodes[n].Y));
        _route.Add(target);
    }

    /// <summary>날 넘김 — 항구에 있으면 하루를 줄이고, 다 머물렀으면 다음 목표로 떠난다.</summary>
    public void NewDay()
    {
        if (DaysInPort == 0 || --DaysInPort > 0) return;
        _towardHome = !_towardHome;
        Plan(_towardHome ? _home : _away);
    }

    /// <summary>한 틱. 바람 · 해류는 제 함대 자리의 것이다.</summary>
    public void Step((int Dir, int Speed) wind, (int Dir, int Speed) current)
    {
        if (Docked || _leg >= _route.Count) return;

        var goal = _route[_leg];
        int dx = goal.X - _cellX;
        if (dx > ChipMap.Width / 2) dx -= ChipMap.Width;
        if (dx < -ChipMap.Width / 2) dx += ChipMap.Width;
        int dy = goal.Y - _cellY;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 1)
        {
            if (++_leg >= _route.Count) DaysInPort = _rng.Next(8) + 8;   // 0x004184B0 의 rand(8)+8
            return;
        }
        Heading = DirOf(dx, dy);

        int speed = Speed(wind, current);
        if (speed == 0) return;
        foreach (int dir in (int[])[Heading, Heading + 1, Heading - 1])
        {
            var (hx, hy) = WindCurTable.Vector(dir & 7);
            if (_sea.Advance(ref _cellX, ref _cellY, ref _subX, ref _subY, hx * speed, hy * speed)) return;
        }
    }

    private int Speed((int Dir, int Speed) wind, (int Dir, int Speed) current)
    {
        if (_ships.Length == 0 || wind.Speed == 0) return 0;
        int angle = Sailing.AngleOf(wind.Dir, Heading);
        int slowest = 0xFF;
        foreach (var ship in _ships)
            slowest = Math.Min(slowest, Sailing.AdmiralShipSpeed(ship.SailKind, ship.Sail, ship.Upwind, angle,
                                                                 wind.Speed, _navLevel, _seamanship));
        int speed = Math.Max(Sailing.MinSpeed, slowest);

        int c = Sailing.AngleOf(current.Dir, Heading);
        if (c is >= 3 and <= 5) speed -= Math.Min(speed - 1, current.Speed);
        else if (c is < 2 or 7) speed += Math.Min(Sailing.MaxSpeed - speed, current.Speed);
        return speed;
    }

    /// <summary>(dx, dy) 를 8방위로 — 긴 축이 짧은 축의 두 배를 넘으면 곧게 간다.</summary>
    private static int DirOf(int dx, int dy)
    {
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        if (Math.Abs(dx) > 2 * Math.Abs(dy)) sy = 0;
        else if (Math.Abs(dy) > 2 * Math.Abs(dx)) sx = 0;
        for (int d = 0; d < 8; d++)
            if (WindCurTable.Dx[d] == sx && WindCurTable.Dy[d] == sy) return d;
        return 0;
    }
}
