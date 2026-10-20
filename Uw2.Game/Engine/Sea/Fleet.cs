using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 바다 위의 함대 하나. 자리·뱃머리·배들을 들고 틱마다 나아간다 — <c>KOUKAI2.EXE 0x004487B0</c>.
/// </summary>
/// <remarks>
/// 자리는 <b>칩 눈금</b>이다(0~2159 / 0~1079) — 괴물표도 항구표도 바람표도 다 이 눈금이라
/// 셈이 한 자로 맞는다. 게임처럼 <b>칸 하나와 칸 안 걸음(0~59)</b>으로 들고 다닌다(함대 기록 +0 · +2 · +0x18 · +0x19).
///
/// 한 틱은 이렇다.
/// <code>
///   빠르기 = Sailing.FleetSpeed(배들, 뱃머리, 바람)          0x00448520
///   걸음   = 방위[뱃머리] x 빠르기 + 방위[해류] x 해류세기   0x00448BC0
///   막히면 뱃머리+1, 뱃머리-1 쪽으로 다시 해 보고, 다 막히면 선다
/// </code>
/// 경도 보정은 없다 — 게임은 칸 눈금 그대로 간다.
/// </remarks>
public sealed class Fleet
{
    /// <summary>
    /// 하루의 틱 수 — 바다에서는 한 틱에 함대가 한 번 움직이고 하루 안 시각(<c>0x004703E4</c>)이 1 오른다
    /// (<c>0x0041A370</c> · <c>0x0041A375</c>). 72 에 닿으면 날을 넘긴다(<c>0x004178C0</c>). 한 시간이 세 틱이다.
    /// </summary>
    public const int TicksPerDay = 72;

    /// <summary>바람을 새로 흔드는 틈 — 시각이 12 의 배수일 때만 흔든다(<c>0x00418D95</c>). 네 시간마다다.</summary>
    public const int GustEvery = 12;

    /// <summary>새 날이 시작하는 시각. 날 넘김이 0 으로 돌리되, 깃발이 서 있으면 24(아침 8시)로 돌린다.</summary>
    public const int MorningTick = 24;

    /// <summary>
    /// 게임 빠르기(환경 설정 0~10, <c>0x0045A1D8</c>). 새 게임은 <c>SDATA.DAT</c> 머리의 5 다.
    /// </summary>
    public int SpeedSetting { get; set; } = 5;

    /// <summary>
    /// 한 틱의 길이(초) — 게임은 틱마다 <c>(빠르기 + 1) x 20</c> 밀리초를 기다린다(<c>0x0043AB60</c>).
    /// 기본 빠르기 5 면 0.12 초, 하루가 8.6 초다.
    /// </summary>
    public double TickSeconds => (SpeedSetting + 1) * 0.020;

    /// <summary>하루 안 시각(0~71). 새 게임은 24(아침 8시)에서 시작한다(<c>SDATA.DAT</c> 머리 +7).</summary>
    public int TimeOfDay { get; private set; } = MorningTick;

    /// <summary>바다에서 보낸 날 수(<c>0x0045BDA8</c>) — 항에서 나갈 때 0 으로(<c>0x00422C36</c>).</summary>
    public int DaysAtSea { get; private set; }

    public void ResetDaysAtSea() => DaysAtSea = 0;

    /// <summary>방위 가짓수.</summary>
    public const int DirCount = Sailing.DirCount;

    private readonly ChipMap _chips;
    private readonly WorldMap _map;
    private readonly WindCurTable? _wind;
    private readonly Random _rng = new();

    private int _cellX, _cellY;
    private int _subX, _subY;       // 0 ~ CellUnits-1

    public Fleet(WorldMap map, ChipMap chips, WindCurTable? wind)
    {
        _map = map; _chips = chips; _wind = wind;
        Weather.Luck = _rng.Next(101);       // 새 게임이 주인공 운을 rand(101) 로 정한다(0x00416A01)
    }

    /// <summary>날씨(폭풍 · 무풍 · 안개).</summary>
    public Weather Weather { get; } = new();

    /// <summary>배들. 가장 느린 배가 함대 빠르기를 정한다.</summary>
    public List<ShipSailing> Ships { get; } = [];

    /// <summary>지금 자리(칩 눈금). 칸 안 걸음까지 얹은 값이라 그릴 때 매끄럽다.</summary>
    public double X => _cellX + _subX / (double)Sailing.CellUnits;
    public double Y => _cellY + _subY / (double)Sailing.CellUnits;

    /// <summary>뱃머리. 0 이 북이고 시계 방향으로 여덟 방위다.</summary>
    public int Heading { get; set; }

    /// <summary>돛을 폈는지. 접으면 그 자리에 선다.</summary>
    public bool UnderSail { get; set; } = true;

    /// <summary>날짜. 날 넘김마다 오른다.</summary>
    public GameDate Date = GameDate.NewGame;

    /// <summary>달(0 부터). 바람 표가 철 따라 갈린다 — 3~8(4월~9월)이 여름 장이다.</summary>
    public int Month => Date.Month;

    /// <summary>지난 틱에 잰 것들 — 상태줄에 적으려고 남긴다.</summary>
    public (int Dir, int Speed) LastWind { get; private set; }
    public (int Dir, int Speed) LastCurrent { get; private set; }
    public int LastSpeed { get; private set; }

    /// <summary>뭍에 막혀 뱃머리대로 못 간 틱이면 참.</summary>
    public bool Blocked { get; private set; }

    /// <summary>
    /// 여관 「숙박」 — 시각을 하루 끝(0x48)으로 밀어 다음 날 아침이 되게 한다(<c>0x0041F2A0</c> → <c>0x0041A2F2</c>).
    /// 항구라 물 · 식량은 줄지 않는다. 달이 바뀌면 달마다 셈을 부른다.
    /// </summary>
    public void SleepUntilMorning()
    {
        TimeOfDay = MorningTick;
        if (Date.NextDay()) Ended?.Invoke();
        Weather.NewDay();
        if (Date.Day == 0) NewMonth?.Invoke();
    }

    /// <summary>해가 <see cref="GameDate.LastYear"/> 를 넘었다 — 서른 해 항해의 끝(<c>0x0043A897</c>, 끝 깃발 0x10).</summary>
    public event Action? Ended;

    /// <summary>세이브에 담을 자리 · 날짜.</summary>
    public SaveGame.FleetState Export() => new()
    {
        CellX = _cellX, CellY = _cellY, SubX = _subX, SubY = _subY, Heading = Heading,
        Year = Date.Year, Month = Date.Month, Day = Date.Day, TimeOfDay = TimeOfDay, DaysAtSea = DaysAtSea,
    };

    public void Import(SaveGame.FleetState s)
    {
        _cellX = Wrap(s.CellX);
        _cellY = Math.Clamp(s.CellY, 0, ChipMap.Height - 1);
        _subX = Math.Clamp(s.SubX, 0, Sailing.CellUnits - 1);
        _subY = Math.Clamp(s.SubY, 0, Sailing.CellUnits - 1);
        Heading = s.Heading;
        Date = new GameDate(s.Year, s.Month, s.Day);
        TimeOfDay = Math.Clamp(s.TimeOfDay, 0, TicksPerDay - 1);
        DaysAtSea = s.DaysAtSea;
        Blocked = false;
    }

    /// <summary>자리를 옮겨 놓는다. 칸 가운데에 세운다.</summary>
    public void PlaceAt(double x, double y)
    {
        _cellX = Wrap((int)Math.Floor(x));
        _cellY = Math.Clamp((int)Math.Floor(y), 0, ChipMap.Height - 1);
        _subX = _subY = Sailing.CellUnits / 2;
        Blocked = false;
    }

    /// <summary>
    /// 그 자리에서 가장 가까운 바다 칸을 찾는다. 항구는 뭍에 붙어 있어 그대로는 못 뜬다.
    /// </summary>
    public bool PlaceNearSea(double x, double y, int reach = 40)
    {
        for (int r = 0; r <= reach; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    double nx = x + dx, ny = y + dy;
                    if (CanSail(nx, ny)) { PlaceAt(nx, ny); return true; }
                }
        return false;
    }

    /// <summary>
    /// 그 칩 자리에 함대가 설 수 있는지 — <c>0x00448310</c>. 함대는 <b>칩 2 x 2</b>(그 자리 · 오른쪽 · 아래 · 오른아래)를
    /// 차지하고 넷이 다 바다여야 한다.
    /// </summary>
    public bool CanSail(double chipX, double chipY)
    {
        int cx = (int)Math.Floor(chipX), cy = (int)Math.Floor(chipY);
        if (cy < 0 || cy + 1 >= ChipMap.Height) return false;
        return ChipMap.CanSail(ChipAt(cx, cy)) && ChipMap.CanSail(ChipAt(cx + 1, cy))
            && ChipMap.CanSail(ChipAt(cx, cy + 1)) && ChipMap.CanSail(ChipAt(cx + 1, cy + 1));
    }

    /// <summary>그 칩 하나가 바다인지 — 상륙은 함대 둘레에 바다가 아닌 칩이 있어야 한다.</summary>
    public bool IsSea(int chipX, int chipY) =>
        chipY >= 0 && chipY < ChipMap.Height && ChipMap.CanSail(ChipAt(chipX, chipY));

    private byte ChipAt(int chipX, int chipY)
    {
        int cx = Wrap(chipX);
        var (lt, rt, lb, rb) = _chips.Quad(_map[cx / ChipMap.PerCell, chipY / ChipMap.PerCell]);
        return (cx & 1) == 0 ? ((chipY & 1) == 0 ? lt : lb)
                             : ((chipY & 1) == 0 ? rt : rb);
    }

    internal static int Wrap(int x) => ((x % ChipMap.Width) + ChipMap.Width) % ChipMap.Width;

    /// <summary>한 틱 나아간다.</summary>
    public void Step()
    {
        Blocked = false;
        if (!UnderSail) { LastSpeed = 0; return; }

        // 바람은 네 시간마다 그 자리의 표에서 새로 흔들고, 그 사이에는 그대로 둔다. 해류는 늘 그 자리 것이다.
        // 날씨가 서 있으면 흔들지 않고, 날씨가 바람을 누르거나 올린 값이 그대로 간다.
        if (TimeOfDay % GustEvery == 0 || !_windSet)
        {
            byte windByte = _wind?.At(WindCurTable.WindPageOf(Month), _cellX, _cellY) ?? 0;
            byte curByte = _wind?.At(WindCurTable.Page.Current, _cellX, _cellY) ?? 0;
            var table = WindCurTable.Split(windByte);
            if (Weather.Current == Weather.Kind.Clear || !_windSet) LastWind = Sailing.Gust(table, _rng);
            Weather.Update(windByte, curByte, LastWind.Speed, table.Speed, _rng);
            LastWind = (LastWind.Dir, Weather.Apply(LastWind.Speed));
            _windSet = true;
        }
        LastCurrent = _wind?.Current(_cellX, _cellY) ?? (0, 0);

        int speed = Sailing.FleetSpeed(Ships, Heading, LastWind.Dir, LastWind.Speed);
        LastSpeed = speed;

        // 뱃머리대로 못 가면 한 칸씩 비껴 본다 — 게임도 +1 다음 -1 이다(0x00448836 · 0x00448857).
        if (!TryMove(Heading, speed))
        {
            Blocked = true;
            if (!TryMove(Heading + 1, speed)) TryMove(Heading - 1, speed);
        }

        if (++TimeOfDay >= TicksPerDay)
        {
            TimeOfDay = 0;
            DaysAtSea++;
            if (Date.NextDay()) Ended?.Invoke();
            Weather.NewDay();
            NewDay?.Invoke();
            if (Date.Day == 0) NewMonth?.Invoke();
        }
    }

    /// <summary>달이 바뀌었을 때(<c>0x0043AA52</c> — 은행 이자 따위).</summary>
    public event Action? NewMonth;

    /// <summary>바다에서 하루가 지났을 때(<c>0x00417930</c> — 물 · 식량 따위).</summary>
    public event Action? NewDay;

    private bool _windSet;

    /// <summary>시각을 사람이 읽게 — 「8시 20분」.</summary>
    public static string Clock(int tick) => $"{tick / 3}시 {tick % 3 * 20:00}분";

    /// <summary>
    /// 그 방위로 한 틱 — <c>0x00448BC0</c>. 막혀서 못 갔으면 false.
    /// </summary>
    /// <remarks>
    /// 칸 안 걸음이 60 을 넘거나 0 아래로 내려가면 그 축으로 한 칸 간다(빠르기가 40 아래라 한 틱에 한 칸까지다).
    /// 새 칸이 뭍이면 <b>두 축으로 한 칸씩 간 대각선 칸</b>을 다시 본다 — 그것도 뭍이면 못 간 것이고,
    /// 열려 있으면 자리는 그대로 두고 넘친 축의 걸음만 칸 끝(0 이나 59)에 붙여 둔다.
    /// </remarks>
    private bool TryMove(int dir, int speed)
    {
        var (hx, hy) = WindCurTable.Vector(dir);
        var (cx, cy) = WindCurTable.Vector(LastCurrent.Dir);
        int vx = hx * speed + cx * LastCurrent.Speed;
        int vy = hy * speed + cy * LastCurrent.Speed;
        return Advance(ref _cellX, ref _cellY, ref _subX, ref _subY, vx, vy);
    }

    /// <summary>
    /// 칸 안 걸음에 (vx, vy) 를 더해 한 틱 옮긴다 — 제 함대(<c>0x00448BC0</c>)와 남 함대(<c>0x00418A90</c>)가 같은 셈이다.
    /// 막혀서 못 갔으면 false.
    /// </summary>
    internal bool Advance(ref int cellX, ref int cellY, ref int subX, ref int subY, int vx, int vy)
    {
        int sx = subX + vx, sy = subY + vy;
        int nx = cellX, ny = cellY;
        bool crossX = false, crossY = false;
        if (sx >= Sailing.CellUnits) { nx++; sx -= Sailing.CellUnits; crossX = true; }
        else if (sx < 0) { nx--; sx += Sailing.CellUnits; crossX = true; }
        if (sy >= Sailing.CellUnits) { ny++; sy -= Sailing.CellUnits; crossY = true; }
        else if (sy < 0) { ny--; sy += Sailing.CellUnits; crossY = true; }

        if (ny < 0 || ny >= ChipMap.Height) return false;

        if ((crossX || crossY) && !CanSail(nx, ny))
        {
            if (!CanSail(cellX + Math.Sign(vx), cellY + Math.Sign(vy))) return false;
            subX = crossX ? (vx > 0 ? Sailing.CellUnits - 1 : 0) : sx;
            subY = crossY ? (vy > 0 ? Sailing.CellUnits - 1 : 0) : sy;
            return true;
        }

        cellX = Wrap(nx); cellY = ny;
        subX = sx; subY = sy;
        return true;
    }

    /// <summary>뱃머리를 한 칸 돌린다.</summary>
    public void Turn(int by) => Heading = ((Heading + by) % DirCount + DirCount) % DirCount;

    /// <summary>방위 이름.</summary>
    public static string DirName(int dir) =>
        new[] { "북", "북동", "동", "남동", "남", "남서", "서", "북서" }[((dir % DirCount) + DirCount) % DirCount];
}
