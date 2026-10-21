using Uw2.Game.Engine;
using Uw2.Game.Engine.Sea;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Rendering;

/// <summary>
/// 「항이동」(<c>0x00448220</c>) — 고른 항구로 저절로 간다. 원본은 함대 +4 에 과녁 자리를 두고 <c>0x0045A1A8</c> 에 0x8000 을 세운 뒤
/// 틱마다 뱃머리를 맞추고(<c>0x00448380</c>) 닿으면 기항을 묻는다(<c>0x00448B5B</c> → <c>0x0041A8D0</c>). 우리는 남 함대와 같은 뱃길 그래프로 간다.
/// </summary>
public sealed partial class SeaMapHost
{
    private readonly List<(int X, int Y)> _autoRoute = [];
    private int _autoLeg, _autoErr, _autoStall, _autoPort = -1;
    private bool _autoNewLeg;
    private int _autoBest = int.MaxValue;
    private int _autoReach = 2;

    /// <summary>항이동 중인 항구, 아니면 -1.</summary>
    public int AutoPort => _autoPort;

    /// <summary>그 항구로 항이동을 건다. 뱃길이 없으면 false.</summary>
    public bool StartAutopilot(int port)
    {
        if (Fleet is not { } f || Ports is not { } pt || port < 0 || port >= pt.Ports.Count) return false;
        _autoRoute.Clear();
        var goal = (pt.Ports[port].X, pt.Ports[port].Y);
        // 제 함대가 설 수 있는 칸으로 길을 찾아(SeaPath) 칸마다 따라간다. 못 찾으면 남 함대 뱃길 그래프로.
        _autoReach = 2;
        if (SeaPath.Find((x, y) => f.CanSail(x, y), ((int)f.X, (int)f.Y), goal) is { Count: > 0 } path)
        {
            _autoRoute.AddRange(path);
            _autoReach = 0;
        }
        else if (Routes is { } g)
        {
            foreach (int n in g.Path(g.Nearest((int)f.X, (int)f.Y), g.Nearest(goal.Item1, goal.Item2)))
                _autoRoute.Add((g.Nodes[n].X, g.Nodes[n].Y));
            _autoRoute.Add(goal);
        }
        else return false;
        _autoLeg = 0; _autoStall = 0; _autoNewLeg = true; _autoBest = int.MaxValue;
        _autoPort = port;
        _portAsked = -1;
        f.Anchored = false;
        f.UnderSail = true;
        return true;
    }

    /// <summary>개발 — 화면 없이 항이동을 돌려 닿기까지의 틱(못 닿으면 -1)과 지나온 자리를 돌려준다.</summary>
    public (int Ticks, List<(int X, int Y)> Track) DevAutopilotRun(int port, int maxTicks)
    {
        var track = new List<(int, int)>();
        if (Fleet is not { } f || !StartAutopilot(port)) return (-1, track);
        for (int t = 0; t < maxTicks; t++)
        {
            if (PortBeside() == port) return (t, track);
            AutopilotTick();
            if (_autoPort < 0) return (PortBeside() == port ? t : -1, track);
            double fx = f.X, fy = f.Y;
            f.Step();
            AutopilotMoved(fx, fy);
            if (t % 72 == 0) track.Add(((int)f.X, (int)f.Y));
            if (t % 72 == 0) DevTrace.Add($"({f.X:0.0},{f.Y:0.0}) 머리 {f.Heading} 빠르기 {f.LastSpeed} 막힘 {f.Blocked} 바람 {f.LastWind} 해류 {f.LastCurrent} 마디 {_autoLeg}/{_autoRoute.Count} 날 {f.DaysAtSea} 선원 {Player.Ships.Sum(s => s.Crew)} 물·식 {Player.Ships.Sum(s => s.Supplies[0])}/{Player.Ships.Sum(s => s.Supplies[1])} 쥐 {Player.Rats} 괴혈 {Player.Scurvy} 방위별 {string.Join(",", Enumerable.Range(0, 8).Select(h => Uw2.Game.Engine.Sea.Sailing.FleetSpeed(f.Ships, h, f.LastWind.Dir, f.LastWind.Speed)))}");
        }
        return (-1, track);
    }

    /// <summary>개발 — 날마다 함대 형편.</summary>
    public List<string> DevTrace { get; } = [];

    /// <summary>개발 — 짠 길(마디 자리)의 몇 곳.</summary>
    public IEnumerable<(int X, int Y)> DevRoute(int every) => _autoRoute.Where((_, i) => i % every == 0);

    /// <summary>항이동을 멈춘다 — 손으로 키를 잡으면.</summary>
    public void StopAutopilot() => _autoPort = -1;

    /// <summary>바다 틱 앞 — 다음 마디로 뱃머리를 맞춘다. 과녁 항구 곁이면 멈추고 기항을 묻는다.</summary>
    private void AutopilotTick()
    {
        if (_autoPort < 0 || Fleet is not { } f) return;
        if (PortBeside() == _autoPort)
        {
            int port = _autoPort;
            _autoPort = -1;
            _portAsked = port;
            f.UnderSail = false;
            Dispatcher.BeginInvoke(() => PortAhead?.Invoke(port));
            return;
        }
        while (_autoLeg < _autoRoute.Count)
        {
            var goal = _autoRoute[_autoLeg];
            int dx = goal.X - (int)f.X, dy = goal.Y - (int)f.Y;
            if (dx > ChipMap.Width / 2) dx -= ChipMap.Width;
            if (dx < -ChipMap.Width / 2) dx += ChipMap.Width;
            // 마디는 남 함대(점) 몫이라 뭍에 가깝다 — 두 칸 곁이면 닿은 것으로, 하루 동안 가까워지지 않으면 건너뛴다.
            // 마지막(항구)은 건너뛰지 않고 사흘 동안 못 가까워지면 그만둔다.
            int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (dist < _autoBest) { _autoBest = dist; _autoStall = 0; }
            bool last = _autoLeg == _autoRoute.Count - 1;
            if (last ? _autoStall < Fleet.TicksPerDay * 3 : dist > _autoReach && _autoStall < Fleet.TicksPerDay)
            {
                f.Heading = AvoidLand(f, SteerToward(dx, dy), goal);
                return;
            }
            _autoLeg++;
            _autoStall = 0;
            _autoBest = int.MaxValue;
            _autoNewLeg = true;
        }
        _autoPort = -1;   // 뱃길 끝까지 왔는데 항구 곁이 아니다 — 손으로 넘긴다
    }

    /// <summary>틱 뒤 — 한 칩도 못 나갔으면 막힌 셈을 센다.</summary>
    private void AutopilotMoved(double fromX, double fromY)
    {
        if (_autoPort < 0 || Fleet is not { } f) return;
        _autoStall++;   // 가까워지면 AutopilotTick 이 0 으로 되돌린다
    }

    /// <summary>
    /// 그 뱃머리 앞 칸이 뭍이면, 배를 댈 수 있는 이웃 칸 가운데 과녁에 가장 가까운 쪽으로 튼다 — 마디가 해안에 바짝 붙어 있어서다(원본 손은 아님).
    /// </summary>
    private static int AvoidLand(Fleet f, int heading, (int X, int Y) goal)
    {
        int fx = (int)f.X, fy = (int)f.Y;
        bool Open(int dir) { var (hx, hy) = WindCurTable.Vector(dir & 7); return f.CanSail(fx + Math.Sign(hx), fy + Math.Sign(hy)); }
        if (Open(heading)) return heading;
        int best = heading;
        double bestD = double.MaxValue;
        for (int d = 0; d < Fleet.DirCount; d++)
        {
            if (!Open(d)) continue;
            var (hx, hy) = WindCurTable.Vector(d);
            double ex = goal.X - (fx + Math.Sign(hx)), ey = goal.Y - (fy + Math.Sign(hy));
            double dist = ex * ex + ey * ey;
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }

    /// <summary>브레젠험처럼 대각과 곧은 걸음을 섞는다(남 함대 <c>0x00461FF8</c> 방위표와 같다).</summary>
    private int SteerToward(int dx, int dy)
    {
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        if (_autoNewLeg) { _autoErr = -(adx / 2); _autoNewLeg = false; }
        int sx, sy;
        if (adx > ady)
        {
            sx = Math.Sign(dx);
            _autoErr += ady;
            if (_autoErr >= 0) { _autoErr -= adx; sy = Math.Sign(dy); } else sy = 0;
        }
        else
        {
            sy = Math.Sign(dy);
            _autoErr += adx;
            if (_autoErr >= 0) { _autoErr -= ady; sx = Math.Sign(dx); } else sx = 0;
        }
        ReadOnlySpan<int> dirs = [7, 0, 1, 6, 8, 2, 5, 4, 3];
        int d = dirs[(sy + 1) * 3 + sx + 1];
        return d == 8 ? Fleet!.Heading : d;
    }
}
