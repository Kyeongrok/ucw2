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

    /// <summary>마지막으로 짠 길 — 같은 자리에서 같은 항구면 다시 찾지 않는다(어림 뒤 바로 출발할 때).</summary>
    private (int Port, int X, int Y, List<(int X, int Y)> Route, bool Cells)? _plan;

    /// <summary>
    /// 그 항구까지 길을 짠다. 제 함대가 설 수 있는 칸으로 찾고(SeaPath, 칸마다 따라간다), 못 찾으면 남 함대 뱃길 그래프로.
    /// 길이 없으면 null.
    /// </summary>
    private (List<(int X, int Y)> Route, bool Cells)? PlanRoute(int port)
    {
        if (Fleet is not { } f || Ports is not { } pt || port < 0 || port >= pt.Ports.Count) return null;
        int fx = (int)f.X, fy = (int)f.Y;
        if (_plan is { } p && p.Port == port && p.X == fx && p.Y == fy) return (p.Route, p.Cells);
        var goal = (pt.Ports[port].X, pt.Ports[port].Y);
        List<(int X, int Y)> route;
        bool cells;
        if (SeaPath.Find((x, y) => f.CanSail(x, y), (fx, fy), goal) is { Count: > 0 } path)
        {
            route = [.. path];
            cells = true;
        }
        else if (Routes is { } g)
        {
            route = [.. g.Path(g.Nearest(fx, fy), g.Nearest(goal.Item1, goal.Item2)).Select(n => (g.Nodes[n].X, g.Nodes[n].Y)), goal];
            cells = false;
        }
        else return null;
        _plan = (port, fx, fy, route, cells);
        return (route, cells);
    }

    /// <summary>
    /// 그 항구까지 어림 날 — 짠 길을 칸 걸음으로 풀어 칸마다 이달 표 바람으로 <see cref="Uw2.Game.Engine.Sea.Sailing.EstimateDays"/> 를 셈한다.
    /// 길이 없으면 -1. 보급 경고에 쓴다(원본에는 없다).
    /// </summary>
    public int AutopilotDays(int port)
    {
        if (Fleet is not { } f || PlanRoute(port) is not { } plan) return -1;
        var page = WindCurTable.WindPageOf(f.Month);
        ReadOnlySpan<int> dirs = [7, 0, 1, 6, 8, 2, 5, 4, 3];
        var steps = new List<(int, int, int, int, int)>();
        int x = (int)f.X, y = (int)f.Y;
        foreach (var next in plan.Route)
        {
            // 마디 사이(남 함대 뱃길)는 여러 칸이라 한 칸씩 다가간다.
            while (x != next.X || y != next.Y)
            {
                int dx = next.X - x;
                if (dx > ChipMap.Width / 2) dx -= ChipMap.Width;
                if (dx < -ChipMap.Width / 2) dx += ChipMap.Width;
                int sx = Math.Sign(dx), sy = Math.Sign(next.Y - y);
                var (wd, ws) = Wind is { } w ? WindCurTable.Split(w.At(page, x, y)) : (0, 0);
                var (cd, cs) = Wind?.Current(x, y) ?? (0, 0);
                steps.Add((dirs[(sy + 1) * 3 + sx + 1], wd, ws, cd, cs));
                x = ((x + sx) % ChipMap.Width + ChipMap.Width) % ChipMap.Width;
                y += sy;
            }
        }
        return Uw2.Game.Engine.Sea.Sailing.EstimateDays(f.Ships, steps);
    }

    /// <summary>그 항구로 항이동을 건다. 뱃길이 없으면 false.</summary>
    public bool StartAutopilot(int port)
    {
        if (Fleet is not { } f || PlanRoute(port) is not { } plan) return false;
        _autoRoute.Clear();
        _autoRoute.AddRange(plan.Route);
        _autoReach = plan.Cells ? 0 : 2;
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
