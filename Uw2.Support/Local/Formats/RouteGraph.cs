namespace Uw2.Support.Local.Formats;

/// <summary>
/// 뱃길 그래프 — <c>DATA1.LZW</c> 4번 청크(마디 768 x 16바이트)와 5번 청크(쓰는 마디 수, 622).
/// </summary>
/// <remarks>
/// 마디 하나(16바이트):
/// <code>
///   +0   x (낱말, 맨 윗 비트는 「제 함대가 아는 마디」 깃발 — 0x0041E360 이 발견한 바다로 세운다)
///   +2   y
///   +4   이웃 마디 넷 (낱말, -1 이면 없음)
///   +12  그 이웃까지의 값 넷 (바이트) — 거리의 네 배 남짓
/// </code>
/// 이웃은 <b>한쪽 길</b>일 수 있다(578 줄이 되돌아오지 않는다). 길 찾기(<c>0x0043B9A0</c>)는 목표에 가장 가까운
/// 마디(<c>0x0043C2D0</c>)에서 시작해 값을 쌓아 가는 넓이 먼저 찾기다. 제 함대만 아는 마디(+0 의 0x8000)로
/// 가리고, 남 함대는 모든 마디를 쓴다. 우리는 같은 값으로 데이크스트라를 돌린다 — 고른 길이 같다.
/// </remarks>
public sealed class RouteGraph
{
    public const int NodeSize = 16, MaxNodes = 768;

    public readonly record struct Node(int X, int Y, short[] Next, byte[] Cost);

    public IReadOnlyList<Node> Nodes { get; }

    private RouteGraph(IReadOnlyList<Node> nodes) => Nodes = nodes;

    /// <summary>4번 청크 날바이트와 쓰는 마디 수로 만든다.</summary>
    public static RouteGraph FromBytes(byte[] raw, int count)
    {
        count = Math.Clamp(count, 0, Math.Min(MaxNodes, raw.Length / NodeSize));
        var nodes = new Node[count];
        for (int i = 0; i < count; i++)
        {
            int o = i * NodeSize;
            var next = new short[4];
            for (int k = 0; k < 4; k++)
            {
                short n = BitConverter.ToInt16(raw, o + 4 + k * 2);
                next[k] = n >= 0 && n < count ? n : (short)-1;
            }
            nodes[i] = new Node(BitConverter.ToUInt16(raw, o) & 0x7FFF, BitConverter.ToInt16(raw, o + 2), next,
                                raw.AsSpan(o + 12, 4).ToArray());
        }
        return new RouteGraph(nodes);
    }

    /// <summary><c>DATA1.LZW</c> 에서 읽는다.</summary>
    public static RouteGraph FromData1(string path)
    {
        var chunks = LsArchive.ReadAll(path);
        int count = chunks[5].Length >= 2 ? BitConverter.ToInt16(chunks[5], 0) : MaxNodes;
        return FromBytes(chunks[4], count);
    }

    /// <summary>그 자리에서 가장 가까운 마디. 지도가 가로로 감기는 것을 친다.</summary>
    public int Nearest(int x, int y)
    {
        int best = -1; long bestD = long.MaxValue;
        for (int i = 0; i < Nodes.Count; i++)
        {
            long dx = Math.Abs(Nodes[i].X - x);
            dx = Math.Min(dx, ChipMap.Width - dx);
            long dy = Nodes[i].Y - y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>마디에서 마디까지 값이 가장 적은 길(두 끝 포함). 못 가면 빈 것.</summary>
    public List<int> Path(int from, int to)
    {
        var path = new List<int>();
        if (from < 0 || to < 0) return path;
        var dist = new int[Nodes.Count];
        var prev = new int[Nodes.Count];
        Array.Fill(dist, int.MaxValue);
        Array.Fill(prev, -1);
        var queue = new PriorityQueue<int, int>();
        dist[from] = 0;
        queue.Enqueue(from, 0);
        while (queue.TryDequeue(out int u, out int du))
        {
            if (du > dist[u]) continue;
            if (u == to) break;
            var n = Nodes[u];
            for (int k = 0; k < 4; k++)
            {
                int v = n.Next[k];
                if (v < 0) continue;
                int alt = du + n.Cost[k];
                if (alt >= dist[v]) continue;
                dist[v] = alt; prev[v] = u;
                queue.Enqueue(v, alt);
            }
        }
        if (dist[to] == int.MaxValue) return path;
        for (int at = to; at >= 0; at = prev[at]) path.Add(at);
        path.Reverse();
        return path;
    }
}
