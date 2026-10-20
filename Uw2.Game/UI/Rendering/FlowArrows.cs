using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Rendering;

/// <summary>
/// 바람·해류를 지도에 얹을 때 쓰는 화살표 그림. 8방위를 미리 돌려 한 줄로 이어 둔다.
/// </summary>
/// <remarks>
/// 원본에는 없는 것이다 — 원본은 화면 귀퉁이에 바람 · 해류 그림 하나씩만 찍는다(<c>KOUKAI2.EXE 0x004366D0</c>).
/// 지도로 볼 때 한눈에 흐름을 읽으라고 얹는 것이라 게임 그림에서 뽑지 않고 여기서 그린다.
/// 3편 <c>cds-helper</c> 의 <c>FlowArrows</c> 를 8방위로 옮겼다.
///
/// 방위는 <see cref="WindCurTable.Vector"/> 와 같은 뜻이다 — <b>밀어가는 쪽</b>을 화살촉이 가리킨다.
/// 색은 넣지 않는다. 알파만 있는 흰 그림이고, 테두리는 알파 안에서 옅은 값(<see cref="EdgeAlpha"/>)으로 남겨
/// 셰이더가 어둡게 깐다 — 남색 바다 위에서도 모양이 보이게.
/// </remarks>
public static class FlowArrows
{
    /// <summary>한 칸의 한 변(점).</summary>
    public const int CellSize = 32;

    /// <summary>칸 수 = 방위 수.</summary>
    public const int Count = WindCurTable.DirCount;

    /// <summary>이어 붙인 그림의 폭.</summary>
    public const int AtlasWidth = CellSize * Count;

    /// <summary>화살표 속 알파와 테두리 알파.</summary>
    public const byte BodyAlpha = 255, EdgeAlpha = 128;

    // 위(-y)를 보는 모양으로 그려 두고 방위만큼 돌린다. 좌표는 칸 가운데가 원점이다.
    private const float Half = CellSize / 2f;
    private const float TipY = -12f, NeckY = -4f, TailY = 11f, BarbX = 6f;
    private const float Body = 2.0f, Edge = 1.3f;

    /// <summary>가장자리를 부드럽게 하려고 한 점을 이만큼 쪼개 본다.</summary>
    private const int Super = 4;

    private static byte[]? _alpha;

    /// <summary>8방위를 가로로 이어 붙인 알파 그림(<see cref="AtlasWidth"/> x <see cref="CellSize"/>).</summary>
    public static byte[] Alpha => _alpha ??= Build();

    private static byte[] Build()
    {
        var buf = new byte[AtlasWidth * CellSize];
        for (int dir = 0; dir < Count; dir++)
        {
            var (vx, vy) = WindCurTable.Vector(dir);
            float len = MathF.Sqrt(vx * vx + vy * vy);
            float ux = vx / len, uy = vy / len;          // 화살촉이 가리킬 쪽(화면 좌표)

            int originX = dir * CellSize;
            for (int y = 0; y < CellSize; y++)
                for (int x = 0; x < CellSize; x++)
                {
                    int inside = 0, ring = 0;
                    for (int sy = 0; sy < Super; sy++)
                        for (int sx = 0; sx < Super; sx++)
                        {
                            float px = x + (sx + 0.5f) / Super - Half;
                            float py = y + (sy + 0.5f) / Super - Half;

                            // 화면 점을 「위를 보는」 제 좌표로 되돌린다. 제 좌표의 -y 가 (ux, uy) 다.
                            float lx = -uy * px + ux * py;
                            float ly = -ux * px - uy * py;

                            float d = Distance(lx, ly);
                            if (d <= Body) inside++;
                            else if (d <= Body + Edge) ring++;
                        }

                    int total = Super * Super;
                    byte v = inside > 0
                        ? (byte)(EdgeAlpha + (BodyAlpha - EdgeAlpha) * inside / total)
                        : (byte)(EdgeAlpha * ring / total);
                    buf[y * AtlasWidth + originX + x] = v;
                }
        }
        return buf;
    }

    /// <summary>화살표 뼈대(자루 하나 + 날개 둘)까지의 거리.</summary>
    private static float Distance(float x, float y)
    {
        float d = Segment(x, y, 0, TailY, 0, TipY);
        d = Math.Min(d, Segment(x, y, 0, TipY, -BarbX, NeckY));
        d = Math.Min(d, Segment(x, y, 0, TipY, BarbX, NeckY));
        return d;
    }

    private static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float wx = px - ax, wy = py - ay;
        float len2 = vx * vx + vy * vy;
        float t = len2 <= 0 ? 0 : Math.Clamp((wx * vx + wy * vy) / len2, 0f, 1f);
        float dx = wx - t * vx, dy = wy - t * vy;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 표 한 칸마다 바람(아래 바이트)과 해류(위 바이트)를 한 낱말로 — 셰이더의 <c>FlowGrid</c> 다.
    /// </summary>
    public static ushort[] Grid(WindCurTable table, int month)
    {
        var grid = new ushort[WindCurTable.Cols * WindCurTable.Rows];
        var windPage = WindCurTable.WindPageOf(month);
        for (int r = 0; r < WindCurTable.Rows; r++)
            for (int c = 0; c < WindCurTable.Cols; c++)
            {
                int x = c * WindCurTable.ChipsPerCell, y = r * WindCurTable.ChipsPerCell;
                byte w = table.At(windPage, x, y), cur = table.At(WindCurTable.Page.Current, x, y);
                grid[r * WindCurTable.Cols + c] = (ushort)(w | cur << 8);
            }
        return grid;
    }
}
