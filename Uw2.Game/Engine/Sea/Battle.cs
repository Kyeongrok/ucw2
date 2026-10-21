namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 해전 한 판(<c>KOUKAI2.EXE 0x00409B70</c>) — 헥스 52 x 48 위에서 네 편이 틱마다 한 척씩 돌아가며 움직인다.
/// </summary>
/// <remarks>
/// 편 0 덮치는 쪽 · 1 맞는 쪽 · 2 덮치는 쪽 원군 · 3 맞는 쪽 원군 — 짝수와 홀수가 서로 적이다(<c>0x004113B0</c>).
/// 판은 틱 고리(<c>0x00411140</c>)가 시각 63 까지 돌고, 틱마다 열 바퀴, 바퀴마다 네 편이 「이번 차례 배」 하나씩 움직인다.
/// 제 함대 기함만 명령을 받고(<c>0x0040D6E0</c>) 나머지는 다 스스로 움직인다(<c>0x0040A830</c>).
/// 자세한 셈은 docs/27 에 있다.
/// </remarks>
public sealed class Battle
{
    public const int Width = 52, Height = 48, Sides = 4, MaxShips = 10, Night = 63, Rounds = 10;

    /// <summary>판 칸 깃발 — 뭍(<c>0x80</c>).</summary>
    public const byte Land = 0x80;

    /// <summary>여섯 방위의 이웃(<c>0x00457C70</c>) — (dx, 짝수 열 dy, 홀수 열 dy). 0 이 위, 시계 방향.</summary>
    private static readonly (int Dx, int DyEven, int DyOdd)[] Neighbors =
        [(0, -1, -1), (1, -1, 0), (1, 0, 1), (0, 1, 1), (-1, 0, 1), (-1, -1, 0)];

    /// <summary>대포 갈래마다 사거리(<c>0x00457278</c>) · 위력(<c>0x00457298</c>).</summary>
    public static readonly int[] GunRange = [0, 3, 2, 2, 5, 5, 2, 5];
    public static readonly int[] GunPower = [0, 20, 14, 10, 10, 4, 4, 20];

    /// <summary>사거리 − 1 줄의 거리 1 ~ 5 무늬(<c>0x00410750</c> 의 바닥 표).</summary>
    private static readonly int[][] RangePattern =
        [[0, 0, 0, 0, 0], [0, 10, 0, 0, 0], [0, 10, 9, 0, 0], [0, 10, 9, 8, 0], [0, 10, 9, 8, 7]];

    /// <summary>
    /// 선장 — 사람 기록의 싸움에 드는 몫. <see cref="Stats"/> 는 +0x1C 부터 여덟(통솔 · 항해술 · 지식 · 직감 · 용기 · 검술 · 매력 · 운).
    /// </summary>
    public sealed class Captain
    {
        public required int Person { get; init; }
        public required int[] Stats { get; init; }
        public int NavLevel { get; init; }
        public int BattleLevel { get; set; }
        public int BattleExp { get; set; }

        /// <summary>포술 기능(+0x30 비트 4) — 포격이 두 배 아프다(<c>0x00412AD0</c>).</summary>
        public bool Gunnery { get; init; }

        public int Stat(int k) => k >= 0 && k < Stats.Length ? Stats[k] : 0;

        /// <summary>주인공이 탄 배면 부선장 · 수석항해사(처지 3 · 5)도 거든다(<c>0x004129E0</c> · <c>0x00412A60</c> · <c>0x00412AD0</c>).</summary>
        public IReadOnlyList<Captain> Aides { get; init; } = [];

        public int Best(int k) => Aides.Select(a => a.Stat(k)).Append(Stat(k)).Max();
        public int BestBattleLevel => Aides.Select(a => a.BattleLevel).Append(BattleLevel).Max();
        public bool AnyGunnery => Gunnery || Aides.Any(a => a.Gunnery);
    }

    /// <summary>배 한 척 — 배 칸(<c>0x00470FA0</c>, 0x24 바이트) 과 그 함대 칸(9 바이트).</summary>
    public sealed class Ship
    {
        public required int Side { get; init; }
        public required int Slot { get; init; }
        public required Captain Captain { get; init; }

        /// <summary>제 함대의 배인지(+0x17 의 0x80).</summary>
        public bool Player { get; init; }

        public required string Name { get; init; }
        public int Crew { get; set; }
        public int Hull { get; set; }
        public int MaxHull { get; set; }
        public int Upwind { get; set; }
        public int Sail { get; set; }

        /// <summary>선종 내구(배 기록 → 선종 +1) — 최대 내구가 깎여도 그 1/4 아래로는 안 간다.</summary>
        public int TypeHull { get; init; }

        /// <summary>선종 선원 자리(+0x12) — 남 배가 버틸 만한지에 든다(<c>0x0040A880</c>).</summary>
        public int TypeCrew { get; init; }

        public int GunKind { get; init; }

        /// <summary>단 대포 문 수(함대 칸 +6).</summary>
        public int Guns { get; init; }

        /// <summary>탄약(+0x18) — 쏠 때마다 하나(<c>0x00412570</c>).</summary>
        public int Shot { get; set; }

        /// <summary>갑판 몫%(+0x21) — 쏘는 사람 · 싸우는 사람.</summary>
        public int DeckShare { get; init; }

        /// <summary>백병 세기(+0x20).</summary>
        public int Melee { get; set; }

        /// <summary>사거리(+0x1F) · 거리 1 ~ 5 의 포격 세기(+0x1A ~ +0x1E).</summary>
        public int Range { get; init; }
        public int[] Fire { get; } = new int[5];

        public int X { get; set; }
        public int Y { get; set; }
        public int Heading { get; set; }

        /// <summary>움직임 쌓임(+0x15) — 곧게 갈수록 오르고 50 이 넘어야 꺾을 수 있다.</summary>
        public int Momentum { get; set; }

        /// <summary>생각 갈래(+0x16 의 아래 넷) — 0/1 피하기 · 2/3 쫓기 · 4/5 기함 곁 · 6/7 기함 따르기 · 8 위 물러나기.</summary>
        public int Mode { get; set; }

        /// <summary>대포를 앞세우는 배(+0x16 의 0x10).</summary>
        public bool Gunner { get; init; }

        /// <summary>노리는 배(+0x0C, +0x16 의 0x80).</summary>
        public Ship? Target { get; set; }

        /// <summary>선원을 다 잃고 칸에 남은 빈 배(<c>0x00412640</c> 이 그 칸을 막는다).</summary>
        public bool Wreck { get; set; }

        /// <summary>싸움에서 빠졌다(+0x16 의 0x40) — 이탈.</summary>
        public bool Left { get; set; }

        /// <summary>바깥 세계로 돌려줄 짝(제 배 · 남 함대 칸).</summary>
        public object? Tag { get; init; }

        /// <summary>살아 있는지(<c>0x00411280</c>).</summary>
        public bool Alive => Crew > 0 && Hull > 0 && !Left;
    }

    /// <summary>한 번 쏜 · 싸운 것 — 화면이 보여 준다.</summary>
    public readonly record struct Hit(Ship By, Ship To, bool Cannon, int Hull, int Crew, int Damage, bool Sunk, bool Taken);

    private readonly byte[] _map = new byte[Width * Height];
    private readonly Func<int, int> _rand;
    private readonly List<Ship>[] _sides = [[], [], [], []];
    private readonly int[] _turn = new int[Sides];

    public Battle(Func<int, int> rand) => _rand = rand;

    public IReadOnlyList<Ship> SideShips(int side) => _sides[side];
    public IEnumerable<Ship> All => _sides.SelectMany(s => s);
    public bool IsLand(int x, int y) => (_map[y * Width + x] & Land) != 0;

    /// <summary>바람 세기 1 ~ 8 · 갈래 0 ~ 5 · 방위마다 값(<c>0x004101C0</c>).</summary>
    public int WindPower { get; private set; }
    public int WindDir { get; private set; }
    private readonly int[] _windCost = new int[6];

    /// <summary>시각(<c>0x004703E4</c>) — 63 이 되면 해가 진다.</summary>
    public int Time { get; set; }

    /// <summary>끝(<c>0x004715A4</c>) — 0 이 아니면 끝났다. <see cref="Outcome"/>.</summary>
    public int Result { get; set; }

    /// <summary>
    /// 끝 값 — 짝수는 제 편이 이긴 쪽으로 친다(<c>0x0040AA10</c> 의 갈래표 <c>0x0040B0B0</c>).
    /// 1 해 짐 · 2 적 기함 이탈 · 3 제 기함 이탈 · 4 적 기함 침몰 · 5 제 기함 침몰 · 6 일기토 승 · 7 일기토 패 · 8 이벤트 · 9 항복.
    /// </summary>
    public static class Outcome
    {
        public const int Sunset = 1, EnemyLeft = 2, PlayerLeft = 3, EnemySunk = 4, PlayerSunk = 5,
                         DuelWon = 6, DuelLost = 7, Event = 8, Surrendered = 9;
    }

    // ── 판 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 판 깔기(<c>0x0040FCA0</c>) — 칩 6 x 6 을 칸 8 x 8 로 편다(칩 cx 는 열 8cx + 2 ~ 8cx + 9). <paramref name="sea"/> 는 (0 ~ 5, 0 ~ 5) 칩이 바다인지.
    /// 바다 칩 둘레 ±4 칸(열 8cx − 2 ~ 8cx + 13, 줄 8cy − 4 ~ 8cy + 11)은 다 바다로 덮는다 — 그 칸의 (x / 8, y / 8) 칩이 뭍이면
    /// rand(2) 로 물결 그림만 바꾼다. 그래서 뭍 칩은 바다 쪽으로 네 칸씩 깎인다. 가장자리 두 열은 옆 열을 베낀다(<c>0x0040FEEB</c>).
    /// </summary>
    public void BuildMap(Func<int, int, bool> sea)
    {
        bool[,] isSea = new bool[6, 6];
        for (int cy = 0; cy < 6; cy++)
            for (int cx = 0; cx < 6; cx++) isSea[cx, cy] = sea(cx, cy);
        for (int cy = 0; cy < 6; cy++)
            for (int cx = 0; cx < 6; cx++)
                for (int y = cy * 8; y < cy * 8 + 8; y++)
                    for (int x = cx * 8 + 2; x < cx * 8 + 10; x++)
                        _map[y * Width + x] = isSea[cx, cy] ? (byte)1 : Land;
        for (int cy = 0; cy < 6; cy++)
            for (int cx = 0; cx < 6; cx++)
            {
                if (!isSea[cx, cy]) continue;
                int x0 = Math.Max(8 * cx - 4, 0) + 2, x1 = Math.Min(12 + 8 * cx, 48) + 2;
                int y0 = Math.Max(8 * cy - 4, 0), y1 = Math.Min(12 + 8 * cy, 48);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        bool landChip = !isSea[Math.Min(5, x / 8), Math.Min(5, y / 8)];
                        _map[y * Width + x] = (byte)(landChip && _rand(2) != 0 ? 0 : 1);
                    }
            }
        for (int y = 0; y < Height; y++)
        {
            _map[y * Width] = _map[y * Width + 1] = _map[y * Width + 2];
            _map[y * Width + Width - 1] = _map[y * Width + Width - 2] = _map[y * Width + Width - 3];
        }
    }

    /// <summary>판 칸 값 — 0x80 이 뭍, 아래 비트는 그림(물결 0 · 1).</summary>
    public byte CellAt(int x, int y) => _map[y * Width + x];

    /// <summary>바람(<c>0x004101C0</c>) — 바람 갈래에서 1 · 2 · 3 · 4 · 3 · 2.</summary>
    public void RollWind()
    {
        WindPower = _rand(8) + 1;
        WindDir = _rand(8) * 5 / 7;
        for (int d = 0; d < 6; d++) _windCost[(WindDir + d) % 6] = d > 3 ? 7 - d : d + 1;
    }

    public void SetWind(int power, int dir)
    {
        WindPower = power; WindDir = dir;
        for (int d = 0; d < 6; d++) _windCost[(WindDir + d) % 6] = d > 3 ? 7 - d : d + 1;
    }

    // ── 배 들이기 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 배를 그 편에 들인다(<c>0x00410350</c>). 생각 갈래(<c>0x00410560</c>) — 용기 &lt; rand(50) 이면 2, 아니면 3.
    /// 대포가 있고 돛만 쓰는 배면 대포를 앞세운다. 기함은 0x22. <paramref name="merchant"/> 는 상선(함대 번호 % 10 이 1 ~ 4, 해적 아님)이라
    /// 처음부터 물러난다(+8). 백병 세기(<c>0x00410900</c>)는 (검술 x 전투 레벨 / 100 + 검술 / 2) x (제 배 · 남 기함 3, 남 배 2) / 3.
    /// </summary>
    public Ship Add(int side, Captain captain, string name, int crew, int hull, int maxHull, int upwind, int sail,
                    int gunKind, int guns, int shot, int deckShare, bool player, bool sailOnly, bool merchant,
                    int typeHull, int typeCrew, object? tag = null)
    {
        var list = _sides[side];
        int range = GunRange[Math.Clamp(gunKind, 0, 7)];
        int melee = (captain.Stat(5) * captain.BattleLevel / 100 + captain.Stat(5) / 2) * (player || list.Count == 0 ? 3 : 2) / 3;
        var ship = new Ship
        {
            Side = side, Slot = list.Count, Captain = captain, Name = name, Player = player,
            Crew = crew, Hull = hull, MaxHull = maxHull, Upwind = upwind, Sail = sail, GunKind = gunKind, Guns = guns,
            Shot = shot, DeckShare = deckShare, Melee = melee, Range = range, TypeHull = typeHull, TypeCrew = typeCrew,
            Gunner = shot > 0 && sailOnly, Tag = tag,
        };
        var row = RangePattern[Math.Max(0, range - 1)];
        for (int d = 0; d < 5; d++) ship.Fire[d] = range == 0 ? 0 : row[d] * GunPower[Math.Clamp(gunKind, 0, 7)];
        int mode = captain.Stat(4) < _rand(50) ? 2 : 3;
        if (merchant) mode |= 8;
        ship.Mode = list.Count == 0 ? 2 : mode;
        list.Add(ship);
        return ship;
    }

    /// <summary>
    /// 펼치기(<c>0x00410960</c>) — 편마다 뱃머리는 함대 뱃머리 x 5 / 7. 기함은 (10 + 8a, 8 + 8b) 에 —
    /// a · b 는 맞는 쪽 함대와의 칩 차 + 2 를 0 ~ 4 로 자른 것 — 놓이고, 막히면 ±8 안에서 다시 고른다.
    /// 나머지는 기함 둘레 −4 ~ +3 에 흩는다.
    /// </summary>
    public void Deploy(int side, int heading8, int chipDx, int chipDy)
    {
        var list = _sides[side];
        if (list.Count == 0) return;
        int heading = heading8 * 5 / 7 % 6;
        foreach (var s in list) s.Heading = heading;
        int bx = 10 + 8 * Math.Clamp(chipDx + 2, 0, 4), by = 8 + 8 * Math.Clamp(chipDy + 2, 0, 4);
        int x = bx, y = by;
        for (int tries = 0; !Free(x, y) && tries < 2000; tries++) { x = _rand(16) + bx - 8; y = _rand(16) + by - 8; }
        Place(list[0], x, y);
        int ax = x - 4, ay = y - 4;
        for (int i = 1, tries = 0; i < list.Count && tries < 5000; tries++)
        {
            int px = _rand(8) + ax, py = _rand(8) + ay;
            if (!Free(px, py)) continue;
            Place(list[i++], px, py);
        }
    }

    private void Place(Ship s, int x, int y) { s.X = x; s.Y = y; }

    public static bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>배가 설 수 있는 칸 — 판 안이고 뭍 · 배가 없다(<c>& 0xE0</c>).</summary>
    public bool Free(int x, int y) => InBounds(x, y) && !IsLand(x, y) && ShipAt(x, y) == null;

    public Ship? ShipAt(int x, int y) => All.FirstOrDefault(s => (s.Alive || s.Wreck) && s.X == x && s.Y == y);

    // ── 헥스 셈 ─────────────────────────────────────────────────────────────

    /// <summary>헥스 거리(<c>0x004112B0</c>) — Y = 2y + (x &amp; 1), 거리 = (max(dx, dY) + dx) / 2.</summary>
    public static int Distance(int x1, int y1, int x2, int y2)
    {
        int ya = 2 * y1 + (x1 & 1), yb = 2 * y2 + (x2 & 1);
        int dx = Math.Abs(x1 - x2), dy = Math.Abs(ya - yb);
        return (Math.Max(dy, dx) + dx) / 2;
    }

    public static (int X, int Y) Step(int x, int y, int dir)
    {
        var n = Neighbors[(dir % 6 + 6) % 6];
        return (x + n.Dx, y + ((x & 1) != 0 ? n.DyOdd : n.DyEven));
    }

    /// <summary>
    /// 쏠 수 있는 자리(<c>0x00411610</c>) — 거리 2 ~ 사거리이고 뱃전 쪽이어야 한다. 뱃머리 % 3 이
    /// 0(위아래)이면 dY ≤ dx, 1(오른위 · 왼아래)이면 오른아래 · 왼위 쪽, 2 면 오른위 · 왼아래 쪽.
    /// </summary>
    public static bool InArc(int x1, int y1, int heading, int range, int x2, int y2)
    {
        int ya = 2 * y1 + (x1 & 1), yb = 2 * y2 + (x2 & 1);
        int dx = Math.Abs(x1 - x2), dy = Math.Abs(ya - yb);
        int d = dy < dx ? dx : (dy + dx) / 2;
        if (d > range || d == 1) return false;
        switch (heading % 3)
        {
            case 0: return dy <= dx;
            case 1:
                if (dy < dx) return false;
                return x2 > x1 ? yb >= ya : x2 < x1 ? yb <= ya : true;
            default:
                if (dy < dx) return false;
                return x2 > x1 ? yb <= ya : x2 < x1 ? yb >= ya : true;
        }
    }

    /// <summary>쏠 수 있는지(<c>0x004115C0</c>) — 탄약이 있고 그 배의 사거리 · 뱃전 안.</summary>
    public static bool CanFire(Ship s, int heading, int x, int y, int tx, int ty) =>
        s.Shot > 0 && s.Range > 0 && InArc(x, y, heading, s.Range, tx, ty);

    /// <summary>움직임 늘기(<c>0x004114B0</c>) — 항해술 x 역풍 / 100, 그것 x 항해 레벨 / 15 를 더하고 100 에서 자른다.</summary>
    public static int MomentumGain(Ship s)
    {
        int v = s.Captain.Stat(1) * s.Upwind / 100;
        return Math.Min(100, v * s.Captain.NavLevel / 15 + v);
    }

    /// <summary>한 차례 걸음(<c>0x00411520</c>) — (돛 x 바람 x 8 / 150 + 20) x 항해술 / 100, 레벨 덤, 10 ~ 25.</summary>
    public int Steps(Ship s)
    {
        int a = s.Sail * WindPower * 8 / 150 + 20;
        int v = s.Captain.Stat(1) * a / 100;
        return Math.Clamp(v * s.Captain.NavLevel / 15 + v, 10, 25);
    }

    /// <summary>한 칸 값(<c>0x00411440</c>) — 곧게면 돛 x 바람값 / 100 + 5, 꺾을 때 바람값 &lt; 3 이면 5, 아니면 10 − 역풍 x 5 / 100.</summary>
    public int Cost(Ship s, int dir, int heading)
    {
        if (dir == heading) return _windCost[dir] * s.Sail / 100 + 5;
        if (_windCost[dir] < 3) return 5;
        return 10 - s.Upwind * 5 / 100;
    }

    // ── 갈 수 있는 칸 ───────────────────────────────────────────────────────

    /// <summary>한 칸에 닿는 길 — 꺾은 수가 적은 것을 남긴다. <see cref="Dirs"/> 는 한 칸씩의 방위.</summary>
    public sealed record Route(int X, int Y, int Heading, int Momentum, int Turns, IReadOnlyList<int> Dirs);

    /// <summary>
    /// 갈 수 있는 칸(<c>0x00411850</c> · <c>0x00411D20</c>) — 걸음 값이 남는 동안 뱃머리 −1 · 0 · +1 로 한 칸씩 간다.
    /// 쌓임이 50 아래면 곧게만 간다. 꺾으면 쌓임이 0 이 된다. 칸마다 닿는 뱃머리별로 꺾은 수가 가장 적은 길을 돌려준다.
    /// 길은 열 칸까지다(<c>0x00471550</c>).
    /// </summary>
    public List<Route> Reach(Ship s)
    {
        var best = new Dictionary<(int, int, int), Route>();
        var start = new Route(s.X, s.Y, s.Heading, s.Momentum, 0, []);
        best[(s.X, s.Y, s.Heading)] = start;
        void Go(Route r, int steps)
        {
            if (r.Dirs.Count >= MaxShips) return;
            int momentum = Math.Min(100, r.Momentum + MomentumGain(s));
            for (int k = -1; k <= 1; k++)
            {
                if (k != 0 && momentum < 50) continue;
                int dir = (r.Heading + k + 6) % 6;
                int cost = Cost(s, dir, r.Heading);
                var (nx, ny) = Step(r.X, r.Y, dir);
                if (!Free(nx, ny) || steps < cost) continue;
                var next = new Route(nx, ny, dir, k == 0 ? momentum : 0, r.Turns + (k == 0 ? 0 : 1), [.. r.Dirs, dir]);
                var key = (nx, ny, dir);
                if (best.TryGetValue(key, out var old) && (old.Turns < next.Turns || old.Turns == next.Turns && old.Dirs.Count <= next.Dirs.Count)) continue;
                best[key] = next;
                Go(next, steps - cost);
            }
        }
        Go(start, Steps(s));
        return [.. best.Values];
    }

    /// <summary>길대로 간다(<c>0x0040A700</c>) — 칸마다 뱃머리를 바꾸고, 같은 쪽이면 쌓임을 늘린다(움직였는데 꺾으면 0).</summary>
    public void Move(Ship s, IReadOnlyList<int> dirs)
    {
        foreach (int dir in dirs)
        {
            bool turned = dir != s.Heading;
            var (nx, ny) = Step(s.X, s.Y, dir);
            if (!Free(nx, ny)) break;
            s.Heading = dir;
            s.X = nx; s.Y = ny;
            s.Momentum = turned ? 0 : Math.Min(100, s.Momentum + MomentumGain(s));
        }
    }

    /// <summary>제자리에서 뱃머리만 한 칸 돌린다(<c>0x0040C3A0</c>) — 쌓임은 0.</summary>
    public void Turn(Ship s, int dir)
    {
        s.Heading = (dir % 6 + 6) % 6;
        s.Momentum = 0;
    }

    // ── 편 ─────────────────────────────────────────────────────────────────

    /// <summary>적 편 둘(<c>0x004113B0</c>) — 짝수 편이면 1 · 3, 홀수면 0 · 2.</summary>
    public IEnumerable<Ship> Enemies(Ship s) =>
        _sides[(s.Side & 1) ^ 1].Concat(_sides[((s.Side & 1) ^ 1) + 2]);

    public IEnumerable<Ship> Friends(Ship s) => _sides[s.Side & 1].Concat(_sides[(s.Side & 1) + 2]);

    public Ship? Flagship(int side) => _sides[side].Count > 0 ? _sides[side][0] : null;

    /// <summary>제 함대 기함 — 명령을 받는 배.</summary>
    public Ship? PlayerFlagship => All.FirstOrDefault(s => s.Player && s.Slot == 0);

    /// <summary>
    /// 노릴 수 있는 적(<c>0x0040C190</c>) — 포격은 뱃전 · 사거리 안, 백병은 이웃 칸.
    /// </summary>
    public (List<Ship> Guns, List<Ship> Board) Targets(Ship s)
    {
        List<Ship> guns = [], board = [];
        foreach (var e in Enemies(s))
        {
            if (!e.Alive) continue;
            if (CanFire(s, s.Heading, s.X, s.Y, e.X, e.Y)) guns.Add(e);
            if (Distance(s.X, s.Y, e.X, e.Y) == 1) board.Add(e);
        }
        return (guns, board);
    }

    /// <summary>판 가장자리 바다인지(<c>0x00409F50</c>) — 「이탈」 은 여기서만 된다.</summary>
    public bool AtEdge(int x, int y) => (x == 0 || y == 0 || x == Width - 1 || y == Height - 1) && !IsLand(x, y);

    // ── 포격 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 포격 세기(<c>0x004128A0</c>).
    /// <code>
    ///   a  = 통솔 x 전투 레벨 / 100 + 통솔 / 2               (제 함대면 동료 둘과 견줘 큰 값)
    ///   문 = min(단 문 / 2, 선원 x 갑판 몫 / 100 / 2)
    ///   b  = 거리 세기 x 문 / 10, 포술이 있으면 (b + 19) / 20, 없으면 (b + 39) / 40
    ///   세기 = a x b / 100 x (rand(검술 / 2) + 50) / 100, 위끝 rand(전투 레벨 / 2) + 50, 아래끝 1
    /// </code>
    /// </summary>
    public int GunDamage(Ship s, int distance)
    {
        var c = s.Captain;
        int lead = c.Best(0);
        int a = lead * c.BestBattleLevel / 100 + lead / 2;
        int guns = Math.Min(s.Guns / 2, s.Crew * s.DeckShare / 100 / 2);
        int fire = distance is >= 1 and <= 5 ? s.Fire[distance - 1] : 0;
        int b = fire * guns / 10;
        b = c.AnyGunnery ? (b + 19) / 20 : (b + 39) / 40;
        int dmg = a * b / 100 * (_rand(c.Best(5) / 2) + 50) / 100;
        dmg = Math.Min(dmg, _rand(c.BattleLevel / 2) + 50);
        return Math.Max(1, dmg);
    }

    /// <summary>
    /// 쏜다(<c>0x00412570</c> → <c>0x004126D0</c>). 내구 −세기, 선원 −(세기 / 2 + rand(3)).
    /// 세기가 10 아래면 다섯에 넷은 그것뿐이고, 아니면 rand(19) 로 덤 피해(최대 내구 · 돛 · 역풍)를 고른다 — 20 넘으면 더, 40 넘으면 다.
    /// 내구가 0 이면 가라앉고, 선원이 0 이면 빈 배가 남는다. 쏠 때마다 탄약 하나.
    /// </summary>
    public Hit Fire(Ship s, Ship t)
    {
        int d = Distance(s.X, s.Y, t.X, t.Y);
        int dmg = GunDamage(s, d);
        int extra = 0;
        if (dmg >= 10 || _rand(5) == 0)
            extra = _rand(19) switch { < 4 => 1, < 8 => 2, < 12 => 4, < 14 => 3, < 16 => 5, < 18 => 6, _ => 7 };
        if (dmg >= 20) extra = _rand(7) switch { < 2 => 3, < 4 => 5, < 6 => 6, _ => 7 };
        if (dmg >= 40) extra = 7;
        int hullLoss = Math.Min(t.Hull, dmg);
        t.Hull = Math.Min(t.Hull - hullLoss, t.MaxHull);
        int crewLoss = Math.Min(t.Crew, dmg / 2 + _rand(3));
        t.Crew -= crewLoss;
        if ((extra & 1) != 0) t.MaxHull = Math.Max(t.TypeHull / 4, t.MaxHull - 1);
        if ((extra & 2) != 0) t.Sail -= Math.Min(t.Sail, _rand(3) + dmg / 2);
        if ((extra & 4) != 0) t.Upwind -= Math.Min(t.Upwind, _rand(3) + dmg / 2);
        s.Shot = Math.Max(0, s.Shot - 1);
        bool sunk = t.Hull == 0, taken = !sunk && t.Crew == 0;
        if (sunk || taken) Gone(t);
        if (s.Player) Experience(s.Captain, sunk || taken ? t.MaxHull * 10 : 10);
        return new Hit(s, t, true, hullLoss, crewLoss, dmg, sunk, taken);
    }

    // ── 백병 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 백병 한 판의 선원 피해(<c>0x00412CD0</c>) — (rand(10) + 선원) x 갑판 몫 / 100 x 백병 세기 / 100 x (rand(검술 / 2) + 50) / 100, 1 ~ 50.
    /// </summary>
    public int MeleeLoss(Ship s)
    {
        int k = (_rand(10) + s.Crew) * s.DeckShare / 100;
        int v = s.Melee * k / 100;
        int dmg = (_rand(s.Captain.Best(5) / 2) + 50) * v / 100;
        return Math.Clamp(dmg, 1, 50);
    }

    /// <summary>
    /// 백병전(<c>0x00412B50</c> · <c>0x00412C80</c>) — 서로 선원을 잃는다. 맞은 배 선원이 0 이면 빼앗았다.
    /// </summary>
    public Hit Board(Ship s, Ship t)
    {
        int lossT = Math.Min(t.Crew, MeleeLoss(s));
        t.Crew -= lossT;
        int lossS = Math.Min(s.Crew, MeleeLoss(t));
        s.Crew -= lossS;
        bool taken = t.Crew == 0;
        if (taken) Gone(t);
        if (s.Player) Experience(s.Captain, taken ? t.MaxHull * 10 : 10);
        return new Hit(s, t, false, 0, lossT, lossS, false, taken);
    }

    /// <summary>
    /// 적 기함끼리 맞닿으면 일기토를 걸 수 있다(<c>0x0040C5E0</c>) — 받아들이려면 적 선원 ≤ 제 선원 + rand(운).
    /// 남이 걸 때(<c>0x00412B98</c>)는 검술 + rand(15) ≥ 주인공 검술이고 rand(적 선원) ≤ 운이어야 한다.
    /// </summary>
    public bool DuelAccepted(Ship s, Ship t) => t.Crew <= s.Crew + _rand(s.Captain.Stat(7));

    public bool WantsDuel(Ship s, Ship playerFlag) =>
        s.Slot == 0 && playerFlag.Slot == 0 && playerFlag.Player
        && s.Captain.Stat(5) + _rand(15) >= playerFlag.Captain.Stat(5)
        && _rand(playerFlag.Crew) <= s.Captain.Stat(7);

    /// <summary>
    /// 빠진 배(<c>0x00412640</c> · <c>0x004125F0</c>) — 판에서 지우고, 그 배를 노리던 배들은 과녁을 놓는다(<c>0x004124B0</c>).
    /// 선원만 다 잃은 배는 빈 배로 칸에 남아 길을 막는다.
    /// </summary>
    private void Gone(Ship t)
    {
        if (t.Hull > 0 && t.Crew == 0) t.Wreck = true;
        foreach (var e in All) if (e.Target == t) e.Target = null;
    }

    /// <summary>이탈(<c>0x00412480</c>).</summary>
    public void Leave(Ship s)
    {
        s.Left = true;
        foreach (var e in All) if (e.Target == s) e.Target = null;
    }

    /// <summary>
    /// 전투 경험(<c>0x00412500</c> → <c>0x0041A4C0</c>) — 경험(+0x28 낱말)이 min(레벨, 18)² x 30 을 넘을 때마다 레벨(+0x25)이 오른다(100 이 끝,
    /// 거기서는 경험을 0 으로). 한 번이라도 오르면 능력치 0 ~ 3 이 rand(3)씩(100 까지), 오른 레벨마다 용기 +rand(3) · 검술 +rand(2)(100 에서 자른다).
    /// </summary>
    public int Experience(Captain c, int exp)
    {
        int ups = 0;
        c.BattleExp = (short)(c.BattleExp + exp);
        while (c.BattleExp >= LevelNeed(c.BattleLevel))
        {
            if (c.BattleLevel == 100) { c.BattleExp = 0; break; }
            c.BattleExp -= LevelNeed(c.BattleLevel);
            c.BattleLevel++;
            ups++;
        }
        if (ups == 0) return 0;
        for (int k = 0; k < 4; k++) c.Stats[k] += Math.Min(100 - c.Stats[k], _rand(3));
        for (int i = 0; i < ups; i++)
        {
            c.Stats[4] = Math.Min(100, c.Stats[4] + _rand(3));
            c.Stats[5] = Math.Min(100, c.Stats[5] + _rand(2));
        }
        return ups;
    }

    /// <summary>다음 레벨까지의 경험(<c>0x0041A490</c>).</summary>
    public static int LevelNeed(int level) { int l = Math.Min(level, 18); return l * l * 30; }

    // ── 끝 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 끝 셈(<c>0x0040A7A0</c>) — 0 편 기함이 쓰러지면 4 + (0 편이 제 함대면 1), 아니면 1 편 기함으로 같은 셈. 이탈이면 − 2.
    /// </summary>
    public bool CheckEnd()
    {
        if (Result != 0) return true;
        foreach (int side in (int[])[0, 1])
        {
            var f = Flagship(side);
            if (f == null || f.Alive) continue;
            bool mine = _sides[side].Any(s => s.Player);
            Result = 4 + (mine ? 1 : 0) - (f.Left ? 2 : 0);
            return true;
        }
        return false;
    }

    // ── 틱 고리 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 다음 차례 배 — 틱 고리(<c>0x00411140</c>)를 한 걸음 돈다. 시각이 63 이면 해가 진다(1).
    /// 돌려주는 배가 제 기함이면 화면이 명령을 받고, 아니면 <see cref="AiTurn"/> 을 부른다.
    /// </summary>
    public Ship? NextShip()
    {
        while (Result == 0)
        {
            if (_round == 0 && _sideIndex == 0) Array.Clear(_turn);
            var list = _sides[_sideIndex];
            Ship? pick = _turn[_sideIndex] < list.Count ? list[_turn[_sideIndex]] : null;
            if (pick != null) _turn[_sideIndex]++;
            if (++_sideIndex == Sides)
            {
                _sideIndex = 0;
                if (++_round == Rounds)
                {
                    _round = 0;
                    if (++Time >= Night) { Result = Outcome.Sunset; return null; }
                }
            }
            if (pick != null && pick.Alive) return pick;
        }
        return null;
    }

    private int _round, _sideIndex;

    // ── 남 배의 생각(0x0040A830) ────────────────────────────────────────────

    /// <summary>
    /// 남 배 한 차례 — 버틸 만하지 않으면 물러나기로(<c>0x0040A880</c>: 선원 &gt; 선종 선원 x 4/10, 내구 &gt; 최대 x 4/10).
    /// 갈 수 있는 칸마다 위험(<c>0x00411A80</c>: 적이 쏠 수 있는 세기 + 이웃이면 백병 세기)과 기회
    /// (<c>0x00411BC0</c>: 거기서 쏠 · 붙을 수 있는 적의 세기, 대포를 앞세우면 포격 x 2, 아니면 백병 x 2)를 매기고,
    /// 갈래대로 칸을 고른 뒤(<c>0x0040A8F0</c>) 꺾음이 적은 길로 가서, 기회가 있는 칸이면 친다(<c>0x00412D60</c>).
    /// </summary>
    public List<Hit> AiTurn(Ship s)
    {
        var hits = new List<Hit>();
        if (!(s.Crew > s.TypeCrew * 4 / 10 && s.Hull > s.MaxHull * 4 / 10)) s.Mode |= 8;
        if (s.Mode >= 8 && AtEdge(s.X, s.Y)) { Leave(s); CheckEnd(); return hits; }

        var routes = Reach(s);
        var threat = new Dictionary<(int, int), int>();
        foreach (var r in routes)
            if (!threat.ContainsKey((r.X, r.Y))) threat[(r.X, r.Y)] = Threat(s, r.X, r.Y);
        var opp = routes.ToDictionary(r => r, r => Chance(s, r.X, r.Y, r.Heading));

        Route pick = routes[0];
        int Moved(Route r) => Distance(s.X, s.Y, r.X, r.Y);
        switch (s.Mode)
        {
            case 0 or 1:
                pick = routes.OrderBy(r => threat[(r.X, r.Y)]).ThenByDescending(Moved).ThenBy(r => r.Turns).First();
                break;
            case 2 or 3:
            {
                var t = s.Target is { Alive: true } tg ? tg : Enemies(s).FirstOrDefault(e => e.Alive);
                if (t != null) pick = Approach(s, routes, opp, t.X, t.Y);
                break;
            }
            case 4 or 5:
            {
                var f = Flagship(s.Side) is { Alive: true } fs && fs != s ? fs : null;
                if (f != null)
                    pick = routes.OrderBy(r => Math.Min(10, Distance(r.X, r.Y, f.X, f.Y))).ThenBy(r => threat[(r.X, r.Y)])
                                 .ThenByDescending(Moved).First();
                break;
            }
            case 6 or 7:
            {
                var f = Flagship(s.Side) is { Alive: true } fs && fs != s ? fs : null;
                if (f != null) pick = Approach(s, routes, opp, f.X, f.Y);
                break;
            }
            default:
                pick = Retreat(s, routes);
                break;
        }
        Move(s, pick.Dirs);
        if (s.Mode >= 8 && AtEdge(s.X, s.Y)) { Leave(s); CheckEnd(); return hits; }

        // 닿은 칸에서 칠 적(0x00412D90) — 노리는 배가 먼저, 대포를 앞세우면 포격부터.
        var targets = (s.Target is { Alive: true } tt ? [tt] : Enumerable.Empty<Ship>()).Concat(Enemies(s).Where(e => e.Alive));
        foreach (var e in targets)
        {
            bool gun = CanFire(s, s.Heading, s.X, s.Y, e.X, e.Y), near = Distance(s.X, s.Y, e.X, e.Y) == 1;
            if (!gun && !near) continue;
            if (near && (!s.Gunner || !gun))
            {
                var pf = PlayerFlagship;
                if (Duels && pf != null && e == pf && WantsDuel(s, pf)) { DuelRequested = (s, e); return hits; }
                hits.Add(Board(s, e));
            }
            else hits.Add(Fire(s, e));
            break;
        }
        CheckEnd();
        return hits;
    }

    /// <summary>일기토를 치를 수 있는지 — 화면이 일기토를 옮기기 전까지는 꺼 두고 백병전으로 친다.</summary>
    public bool Duels { get; set; }

    /// <summary>남이 제 기함에 일기토를 걸었다 — 화면이 일기토를 치르고 <see cref="DuelOver"/> 를 부른다.</summary>
    public (Ship By, Ship To)? DuelRequested { get; set; }

    /// <summary>일기토 끝(<c>0x00414FC0</c>) — 주인공이 이기면 6, 지면 7.</summary>
    public void DuelOver(bool playerWon)
    {
        DuelRequested = null;
        Result = playerWon ? Outcome.DuelWon : Outcome.DuelLost;
    }

    /// <summary>그 칸의 위험(<c>0x00411B40</c>) — 그 칸을 쏠 수 있는 적의 거리 세기 + 거리 1 이면 백병 세기.</summary>
    private int Threat(Ship s, int x, int y)
    {
        int v = 0;
        foreach (var e in Enemies(s))
        {
            if (!e.Alive || Math.Abs(e.X - x) > 5 || Math.Abs(e.Y - y) > 5) continue;
            int d = Distance(e.X, e.Y, x, y);
            if (d == 0) continue;
            if (CanFire(e, e.Heading, e.X, e.Y, x, y)) v += e.Fire[Math.Min(5, d) - 1];
            if (d == 1) v += e.Melee;
        }
        return v;
    }

    /// <summary>그 칸 · 뱃머리에서의 기회(<c>0x00411BC0</c>) — 생각 갈래 &amp; 3 이 0 이면 없다.</summary>
    private int Chance(Ship s, int x, int y, int heading)
    {
        if ((s.Mode & 3) == 0) return 0;
        int gunW = s.Gunner ? 2 : 1, meleeW = s.Gunner ? 1 : 2, best = 0;
        foreach (var e in Enemies(s))
        {
            if (!e.Alive) continue;
            int d = Distance(x, y, e.X, e.Y);
            if (CanFire(s, heading, x, y, e.X, e.Y)) best = Math.Max(best, s.Fire[Math.Min(5, d) - 1] * gunW);
            if (d == 1) best = Math.Max(best, s.Melee * meleeW);
        }
        return best;
    }

    /// <summary>
    /// 다가가기(<c>0x0040A2F0</c>) — max(과녁 거리, 사거리) 가 작은 칸, 같으면 기회가 큰 칸, 그것도 같으면 멀리 간 칸.
    /// </summary>
    private Route Approach(Ship s, List<Route> routes, Dictionary<Route, int> opp, int tx, int ty) =>
        routes.OrderBy(r => Math.Max(Distance(r.X, r.Y, tx, ty), s.Range))
              .ThenByDescending(r => opp[r])
              .ThenByDescending(r => Distance(s.X, s.Y, r.X, r.Y))
              .ThenBy(r => r.Turns).First();

    /// <summary>
    /// 물러나기(<c>0x00409F90</c>) — 판 가장자리 바다 칸에 닿을 수 있으면 그 가운데 멀리 간 칸, 아니면
    /// 가장자리 열두 자리(네 귀 · 네 변 가운데 둘씩, <c>0x0040A0A0</c>) 가운데 가장 가까운 데로.
    /// </summary>
    private Route Retreat(Ship s, List<Route> routes)
    {
        var edge = routes.Where(r => AtEdge(r.X, r.Y)).ToList();
        if (edge.Count > 0) return edge.OrderByDescending(r => Distance(s.X, s.Y, r.X, r.Y)).First();
        (int X, int Y)[] marks = [(0, 0), (51, 0), (0, 47), (51, 47), (25, 0), (26, 0), (0, 23), (0, 24), (51, 23), (51, 24), (25, 47), (26, 47)];
        var open = marks.Where(m => !IsLand(m.X, m.Y)).ToArray();
        if (open.Length == 0) return routes[0];
        return routes.OrderBy(r => open.Min(m => Distance(r.X, r.Y, m.X, m.Y))).ThenBy(r => r.Turns).First();
    }
}
