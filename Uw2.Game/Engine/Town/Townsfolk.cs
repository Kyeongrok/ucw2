namespace Uw2.Game.Engine.Town;

/// <summary>
/// 마을 사람 여덟 — <c>0x0049F580</c>, 셋 바이트씩(왼위 칸 x · y · 쪽). 0 ~ 3 은 돌아다니고 4 ~ 7 은 문 곁에 서 있다. x 0xFF 는 없음.
/// </summary>
/// <remarks>
/// <code>
///   내놓기(0x004361A0, 바다에서 항구에 들 때)  교역소 · 주점 · 조선소 · 여관 문(i = 0, 1, 2, 4)마다
///        걷는 이 i   = (문 x − 2, 문 y + 1), 쪽 2(아래)        조선소의 걷는 이는 문 자리 그대로
///        서 있는 이 i = (문 x + 2, 문 y + 1), 쪽 = i(그림 갈래)  여섯째(조선소 곁)는 없음
///   그 나라가 주인공 나라의 기항을 막으면(나라 표 +0x12 + 주인공 나라 &amp; 0x10) 주인공이 선 항 문 앞 둘레에
///        서 있는 이 셋(4 ~ 6)을 (문 x − 2 / 0 / + 2, 문 y + 2 / 3 / 2), 쪽 2(창 든 이)로 세운다 — 기항 금지 경비병.
///   걸음(0x00432C80, 틱마다)  걷는 이마다 rand(10) == 0 이면 쪽을 rand(4) 로 바꾸고, rand(3) == 0 이면 한 칸 간다
///        (0 위 · 1 오른쪽 · 2 아래 · 3 왼쪽). 사람과 겹치거나 밖이거나 못 밟는 칸이면 쪽을 (쪽 − 1 + rand(3)) &amp; 3 로 튼다.
///   그림(32 장 판의 8 부터 = folk.png)  걷는 이 (쪽 + (번호 &amp; 1) x 4) x 2 + 칸 홀짝, 서 있는 이 16 + 쪽 x 2 + rand(2)
///   말 갈래(0x00432F20)  번호 + 1. 다만 4 넘는 번호가 쪽 2(경비병)면 7.
/// </code>
/// 걸음 판정은 사람과 같다 — 그림 밑줄(y + 1)의 두 칸 x · x + 1(<c>0x00432EB0</c>).
/// </remarks>
public sealed class Townsfolk
{
    public const int Count = 8, Walkers = 4, Absent = 0xFF;

    public readonly record struct Person(int X, int Y, int Dir)
    {
        public bool Here => X != Absent;
    }

    private readonly Person[] _people = new Person[Count];

    public IReadOnlyList<Person> People => _people;

    /// <summary>
    /// 항구에 들 때 문 곁에 세운다(<c>0x004361A0</c>). <paramref name="doors"/> 는 건물 열둘의 문(왼위 칸), <paramref name="banned"/> 면
    /// 항(3) 문 앞에 경비병 셋.
    /// </summary>
    public void Spawn(IReadOnlyList<(int X, int Y)?> doors, bool banned)
    {
        Array.Fill(_people, new Person(Absent, Absent, 2));
        int[] at = [0, 1, 2, 4];
        for (int i = 0; i < Walkers; i++)
        {
            if (at[i] >= doors.Count || doors[at[i]] is not { } d) continue;
            _people[i] = i == 2 ? new Person(d.X, d.Y, 2) : new Person(d.X - 2, d.Y + 1, 2);
            _people[Walkers + i] = new Person(d.X + 2, d.Y + 1, i);
        }
        _people[6] = new Person(Absent, Absent, 2);
        if (banned && doors.Count > 3 && doors[3] is { } h)
            for (int k = 0; k < 3; k++)
                _people[Walkers + k] = new Person(h.X + (k - 1) * 2, h.Y + 2 + (k & 1), 2);
    }

    /// <summary>그 왼위 칸에 선 사람 둘레(±1)에 걸린 마을 사람 번호, 없으면 -1(<c>0x00432F20</c>). <paramref name="self"/> 는 빼고 본다.</summary>
    public int At(int x, int y, int self = -1)
    {
        for (int i = 0; i < Count; i++)
        {
            var p = _people[i];
            if (i == self || !p.Here) continue;
            if (x >= p.X - 1 && x <= p.X + 1 && y >= p.Y - 1 && y <= p.Y + 1) return i;
        }
        return -1;
    }

    /// <summary>말 갈래 1 ~ 8(<c>0x00432FB9</c>).</summary>
    public int Kind(int index) => index > 3 && _people[index].Dir == 2 ? 7 : index + 1;

    /// <summary>
    /// 한 틱(<c>0x00432CEF</c>) — 걷는 이 넷을 옮긴다. <paramref name="canStand"/> 는 그림 왼위 칸(x, y)에 설 수 있는지,
    /// <paramref name="hero"/> 는 주인공 그림 왼위 칸(겹치면 못 간다).
    /// </summary>
    public void Step(Func<int, int, bool> canStand, (int X, int Y) hero, Func<int, int> rand)
    {
        for (int i = 0; i < Walkers; i++)
        {
            var p = _people[i];
            if (!p.Here) continue;
            int dir = rand(10) == 0 ? rand(4) : p.Dir;
            int nx = p.X, ny = p.Y;
            if (rand(3) == 0)
            {
                if ((dir & 1) != 0) nx += dir == 1 ? 1 : -1;
                else ny += dir == 0 ? -1 : 1;
            }
            bool clash = At(nx, ny, i) >= 0
                         || (Math.Abs(nx - hero.X) <= 1 && Math.Abs(ny - hero.Y) <= 1 && (nx, ny) != (p.X, p.Y));
            if (clash || nx < 0 || ny < 0 || nx >= 0x5E || ny >= 0x5E || !canStand(nx, ny))
                _people[i] = p with { Dir = (dir - 1 + rand(3)) & 3 };
            else
                _people[i] = new Person(nx, ny, dir);
        }
    }

    /// <summary>그림 번호(마을 사람 판 안, 0 ~ 23).</summary>
    public int Frame(int index, Func<int, int> rand)
    {
        var p = _people[index];
        return index < Walkers ? (p.Dir + (index & 1) * 4) * 2 + 1 - ((p.X + p.Y) & 1) : 16 + (p.Dir & 3) * 2 + rand(2);
    }

    public byte[] Export()
    {
        var b = new byte[Count * 3];
        for (int i = 0; i < Count; i++) { b[i * 3] = (byte)_people[i].X; b[i * 3 + 1] = (byte)_people[i].Y; b[i * 3 + 2] = (byte)_people[i].Dir; }
        return b;
    }

    public void Import(byte[]? b)
    {
        if (b is not { Length: Count * 3 }) return;
        for (int i = 0; i < Count; i++) _people[i] = new Person(b[i * 3], b[i * 3 + 1], b[i * 3 + 2]);
    }
}
