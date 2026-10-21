using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 항이동 길 찾기 — 칩 눈금에서 A*(여덟 방위, 경도는 감아 돈다). 원본 <c>0x00448380</c> 은 남 함대 뱃길 표를 쓰지만 그 마디는 점(남 함대) 몫이라
/// 2 x 2 를 차지하는 제 함대가 해안 굽이에 걸린다. 그래서 제 함대가 설 수 있는 칸(<see cref="Fleet.CanSail"/>)으로 길을 다시 찾는다(원본 손은 아님).
/// </summary>
public static class SeaPath
{
    private static readonly (int Dx, int Dy)[] Steps = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>
    /// <paramref name="from"/> 에서 <paramref name="goal"/> 둘레 <paramref name="reach"/> 칸 안의 설 수 있는 칸까지의 길(시작은 빼고).
    /// 못 찾으면 null. <paramref name="maxExpand"/> 칸을 넘게 펼치면 그만둔다.
    /// </summary>
    public static List<(int X, int Y)>? Find(Func<int, int, bool> canSail, (int X, int Y) from, (int X, int Y) goal, int reach = 2,
                                             int maxExpand = 2_000_000)
    {
        const int W = ChipMap.Width, H = ChipMap.Height;
        int Wrap(int x) => ((x % W) + W) % W;
        int Dist(int ax, int ay, int bx, int by)
        {
            int dx = Math.Abs(ax - bx);
            dx = Math.Min(dx, W - dx);
            return Math.Max(dx, Math.Abs(ay - by));
        }
        var g = new Dictionary<int, int>();
        var came = new Dictionary<int, int>();
        var open = new PriorityQueue<int, int>();
        int start = from.Y * W + Wrap(from.X);
        g[start] = 0;
        open.Enqueue(start, Dist(from.X, from.Y, goal.X, goal.Y));
        int expanded = 0;
        while (open.TryDequeue(out int cur, out _))
        {
            int cx = cur % W, cy = cur / W;
            if (Dist(cx, cy, goal.X, goal.Y) <= reach)
            {
                var path = new List<(int, int)>();
                for (int n = cur; n != start; n = came[n]) path.Add((n % W, n / W));
                path.Reverse();
                return path;
            }
            if (++expanded > maxExpand) return null;
            int gc = g[cur];
            foreach (var (dx, dy) in Steps)
            {
                int nx = Wrap(cx + dx), ny = cy + dy;
                if (ny < 0 || ny >= H || !canSail(nx, ny)) continue;
                if (dx != 0 && dy != 0 && (!canSail(Wrap(cx + dx), cy) || !canSail(cx, cy + dy))) continue;   // 모서리 끊어 가기 없음
                int n = ny * W + nx, cost = gc + (dx != 0 && dy != 0 ? 14 : 10);
                if (g.TryGetValue(n, out int old) && old <= cost) continue;
                g[n] = cost;
                came[n] = cur;
                open.Enqueue(n, cost + 10 * Dist(nx, ny, goal.X, goal.Y));
            }
        }
        return null;
    }
}
