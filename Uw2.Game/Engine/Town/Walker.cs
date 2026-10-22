using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Town;

/// <summary>
/// 도시 안을 걸어 다니는 사람. 자리는 <b>항구지도 칸</b>(96 x 96)이다.
/// </summary>
/// <remarks>
/// 밟을 수 있는지는 게임 판정(<c>KOUKAI2.EXE 0x00433580</c>)을 그대로 쓴다 — 그림이 2 x 2 칸이고
/// <b>밑줄 두 칸(왼발 · 오른발)</b>의 칩을 벌의 걸음 경계(<see cref="WalkBounds"/>)에 댄다.
/// 자리 X 는 그림 가로 한가운데(두 발 사이)이고 Y 는 발이 밟은 줄이다.
///
/// 걸음은 원본처럼 <b>한 칸씩</b>이다 — 걸음 손(<c>0x00432FE0</c>)은 쪽 넷(0 위 · 1 오른 · 2 아래 · 3 왼) 가운데 하나로 자리에 −1 ~ 1 을 더하고,
/// 막히면 그 자리에 선다(벽을 따라 미끄러지지 않는다). 한 칸 걸음마다 <see cref="CellTicks"/> 틱을 기다려 키를 누르고 있는 동안 걷는 빠르기를 맞춘다.
/// </remarks>
public sealed class Walker
{
    /// <summary>한 틱에 흐르는 시간(초).</summary>
    public const double TickSeconds = 1.0 / 30;

    /// <summary>한 칸 걸음 사이의 틱 — 예전 미끄러지던 걸음(한 틱 0.12 칸)과 같은 빠르기로 잡았다.</summary>
    public const int CellTicks = 8;

    /// <summary>사람 그림 한 장의 크기(점).</summary>
    public const int SpriteW = AssetPack.CharW, SpriteH = AssetPack.CharH;

    /// <summary>보는 쪽.</summary>
    public enum Facing { Down = 0, Left = 1, Right = 2, Up = 3 }

    private readonly byte[] _cells;
    private readonly WalkBounds _bounds;
    private readonly (int X, int Y)?[] _doors;

    public Walker(byte[] portCells, WalkBounds bounds, (int X, int Y)?[]? doors = null)
    {
        _cells = portCells; _bounds = bounds; _doors = doors ?? [];
    }

    /// <summary>
    /// 지금 선 문의 건물 번호, 문이 아니면 -1 — <c>0x00432BF0</c>. 사람 그림 왼위 칸이 문 자리와 같아야 한다.
    /// 그림 왼위는 <c>(round(X) - 1, floor(Y) - 1)</c> 다(발이 그림 밑줄이다).
    /// </summary>
    public int Building
    {
        get
        {
            int x = (int)Math.Round(X) - 1, y = (int)Math.Floor(Y) - 1;
            for (int i = 0; i < _doors.Length; i++)
                if (_doors[i] is { } d && d.X == x && d.Y == y) return i;
            return -1;
        }
    }

    /// <summary>
    /// 건물에서 나온다 — 한 칸 아래로 물린다. 게임도 건물 창을 닫으면 사람을 한 줄 내린다(<c>0x00431FA0</c>).
    /// </summary>
    public void StepOutOfDoor()
    {
        if (CanWalk(X, Y + 1)) Y += 1;
        Face = Facing.Down;
        Moving = false;
    }

    /// <summary>자리를 그대로 옮긴다 — 마을 사람에게 막혀 되돌릴 때.</summary>
    public void Put(double x, double y)
    {
        X = x; Y = y;
        Moving = false;
    }

    /// <summary>지금 자리(칸).</summary>
    public double X { get; private set; }
    public double Y { get; private set; }

    /// <summary>보는 쪽.</summary>
    public Facing Face { get; private set; } = Facing.Down;

    /// <summary>걷고 있는지. 걸을 때만 걸음 그림이 바뀐다.</summary>
    public bool Moving { get; private set; }

    /// <summary>걸은 칸 수. 그림을 갈아 끼우고 마을 사람에게 말을 걸 사이를 재는 데 쓴다.</summary>
    public double Walked { get; private set; }

    /// <summary>다음 칸 걸음까지 남은 틱.</summary>
    private int _wait;

    /// <summary>막혀서 못 간 틱이면 참.</summary>
    public bool Blocked { get; private set; }

    /// <summary>그 자리에 설 수 있는지. 두 발이 밟는 칸은 <c>round(x)-1</c> 과 <c>round(x)</c> 다.</summary>
    public bool CanWalk(double x, double y)
    {
        int right = (int)Math.Round(x), left = right - 1, row = (int)Math.Floor(y);
        if (left < 0 || row < 0 || right >= PortMap.Size || row >= PortMap.Size) return false;
        return _bounds.CanStand(_cells[row * PortMap.Size + left], _cells[row * PortMap.Size + right]);
    }

    /// <summary>그 자리에서 가장 가까운 밟을 수 있는 칸에 세운다.</summary>
    public bool PlaceNear(double x, double y, int reach = 48)
    {
        for (int r = 0; r <= reach; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    double nx = Math.Round(x) + dx, ny = Math.Floor(y) + dy + 0.5;
                    if (CanWalk(nx, ny)) { X = nx; Y = ny; return true; }
                }
        return false;
    }

    /// <summary>
    /// 한 틱 걷는다. <paramref name="dx"/>·<paramref name="dy"/> 는 -1 · 0 · 1 이다. 원본은 쪽이 넷이라 둘 다 서면 가로를 쓴다.
    /// 칸 걸음 사이(<see cref="CellTicks"/>)에는 자리를 그대로 두고 기다린다.
    /// </summary>
    public void Step_(int dx, int dy)
    {
        Blocked = false;
        if (dx != 0) dy = 0;
        Moving = dx != 0 || dy != 0;
        if (!Moving) { _wait = 0; return; }

        if (dy > 0) Face = Facing.Down;
        else if (dy < 0) Face = Facing.Up;
        else if (dx < 0) Face = Facing.Left;
        else Face = Facing.Right;

        if (_wait > 0) { _wait--; return; }

        if (!CanWalk(X + dx, Y + dy)) { Blocked = true; Moving = false; return; }
        X += dx; Y += dy;
        Walked += 1;
        _wait = CellTicks - 1;
    }

    /// <summary>
    /// 지금 그릴 그림 번호. <b>보는 쪽마다 두 장</b>을 번갈아 밟는다.
    /// </summary>
    /// <remarks>
    /// 어느 장이 어느 쪽인지는 <see cref="AssetPack.CharPose"/> 가 안다. 서 있을 때는
    /// 앞엣것으로 굳혀 둔다 — 멈춰 서서 발을 떠는 꼴을 안 만들려는 것이다. 걸을 때는 한 칸마다 발을 바꾼다.
    /// </remarks>
    public int Frame => AssetPack.CharPose((int)Face, Moving && (int)Walked % 2 == 1);
}
