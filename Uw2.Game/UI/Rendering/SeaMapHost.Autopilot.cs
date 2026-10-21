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

    /// <summary>항이동 중인 항구, 아니면 -1.</summary>
    public int AutoPort => _autoPort;

    /// <summary>그 항구로 항이동을 건다. 뱃길이 없으면 false.</summary>
    public bool StartAutopilot(int port)
    {
        if (Fleet is not { } f || Routes is not { } g || Ports is not { } pt || port < 0 || port >= pt.Ports.Count) return false;
        _autoRoute.Clear();
        foreach (int n in g.Path(g.Nearest((int)f.X, (int)f.Y), g.Nearest(pt.Ports[port].X, pt.Ports[port].Y)))
            _autoRoute.Add((g.Nodes[n].X, g.Nodes[n].Y));
        _autoRoute.Add((pt.Ports[port].X, pt.Ports[port].Y));
        _autoLeg = 0; _autoStall = 0; _autoNewLeg = true;
        _autoPort = port;
        _portAsked = -1;
        f.Anchored = false;
        f.UnderSail = true;
        return true;
    }

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
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > 1 && _autoStall < Fleet.TicksPerDay)
            {
                f.Heading = SteerToward(dx, dy);
                return;
            }
            _autoLeg++;
            _autoStall = 0;
            _autoNewLeg = true;
        }
        _autoPort = -1;   // 뱃길 끝까지 왔는데 항구 곁이 아니다 — 손으로 넘긴다
    }

    /// <summary>틱 뒤 — 한 칩도 못 나갔으면 막힌 셈을 센다.</summary>
    private void AutopilotMoved(double fromX, double fromY)
    {
        if (_autoPort < 0 || Fleet is not { } f) return;
        _autoStall = (int)f.X == (int)fromX && (int)f.Y == (int)fromY ? _autoStall + 1 : 0;
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
