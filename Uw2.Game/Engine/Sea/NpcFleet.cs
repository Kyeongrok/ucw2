using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 남의 함대 하나 — 뱃길 그래프를 따라 목표 항구로 간다(<c>KOUKAI2.EXE 0x00418250</c> 의 한 바퀴).
/// </summary>
/// <remarks>
/// 틱마다 모든 남 함대가 한 걸음씩 간다. 빠르기는 <c>0x00418020</c> — 제 함대 셈과 효율표는 같되
/// 선원 · 짐을 안 보고 <b>제독 하나</b>의 항해 레벨 · 항해술을 쓴다. 바람 · 해류는 <b>제 함대 자리의 것</b>을
/// 모두가 같이 쓴다(<c>0x0049F5A0</c> · <c>0x0049F59C</c> 를 그대로 읽는다). 해류는 걸음에 안 더하고 빠르기에 더하고 뺀다.
/// <code>
///   해류 각 3~5   빠르기 -= min(빠르기 - 1, 해류세기)
///   해류 각 0·1·7 빠르기 += min(40 - 빠르기, 해류세기)
/// </code>
/// 뱃머리는 다음 마디(마지막엔 목표 자리)를 보고 정한다(<c>0x00448380</c>). 가로 · 세로 차를 쌓임 값(함대 +0x0E)으로
/// 비스듬히 나눠 가는 선 긋기다 — 긴 축은 늘 한 칸, 짧은 축은 쌓임 값이 0 을 넘을 때만 한 칸. 마디가 바뀌면
/// 쌓임 값을 <c>-(가로 차 / 2)</c> 로 새로 잡는다(깃발 0x200). 부호 둘을 3 x 3 표 <c>0x00461FF8</c>
/// (<c>7 0 1 / 6 8 2 / 5 4 3</c>)로 방위로 바꾼다.
///
/// 닿은 뒤의 일(<c>0x004184B0</c>)은 움직임 갈래(+0x1B)마다 다르다. 0 갈래는 항구 번호 <c>+0x1C</c>(고향)로
/// 목표를 바꾸고 <c>rand(8) + 8</c> 을 +0x27 에 둔다. <b>우리는 처음 목표 항구와 고향을 오가며, 닿으면 그만큼의 날을
/// 항구에 머문다</b> — 새 목표를 고르는 손은 아직 안 옮겼다.
/// </remarks>
public sealed class NpcFleet
{
    private readonly Fleet _sea;
    private readonly RouteGraph _graph;
    private readonly Random _rng;
    private readonly ShipSailing[] _ships;
    private readonly int _navLevel, _seamanship;
    private readonly (int X, int Y) _home, _away;

    private int _cellX, _cellY, _subX = Sailing.CellUnits / 2, _subY = Sailing.CellUnits / 2;
    private readonly List<(int X, int Y)> _route = [];
    private int _leg;
    private int _err;                 // 함대 +0x0E
    private bool _newLeg = true;      // 함대 +0x0C 의 0x200
    private bool _towardHome;

    public NpcFleet(Fleet sea, RouteGraph graph, Random rng, StartData.Fleet record, StartData start,
                    (int X, int Y) home, (int X, int Y) target)
    {
        _sea = sea; _graph = graph; _rng = rng;
        Record = record;
        Slots = [.. record.Slots.Where(s => s.Present)];
        Index = record.Index;
        _cellX = record.X; _cellY = record.Y;
        Heading = record.Heading & 7;
        Admiral = record.Admiral;
        var admiral = start.People[Math.Clamp(record.Admiral, 0, start.People.Count - 1)];
        _navLevel = admiral.NavLevel; _seamanship = admiral.Seamanship;
        _ships = [.. record.Slots.Where(s => s.Present).Select(s =>
        {
            var t = start.ShipTypes[start.GuessType(s)];
            return new ShipSailing(t.SailKind, s.Sail, s.Upwind, false, 0, 0, 0, 0, 0, 0, 0);
        })];
        _home = home; _away = target;
        Goal = record.Home;
        int fo = record.Index * StartData.FleetSize;
        if (fo + StartData.FleetSize <= start.FleetBytes.Length)
        {
            Gold = start.FleetBytes[fo + 0x24] | start.FleetBytes[fo + 0x25] << 8;
            for (int i = 0; i < Items.Length; i++) Items[i] = start.FleetBytes[fo + 0x1D + i];
            CargoGood = start.FleetBytes[fo + 0x21] == 0xFF ? -1 : start.FleetBytes[fo + 0x21];
            CargoAmount = start.FleetBytes[fo + 0x22] | start.FleetBytes[fo + 0x23] << 8;
        }
        Plan(target);
    }

    public int Index { get; }

    /// <summary>처음 함대 기록(SDATA).</summary>
    public StartData.Fleet Record { get; }

    /// <summary>배 칸(함대 +0x2B) — 해전이 선원 · 내구를 깎는다. 다 잃으면 함대가 바다에서 빠진다.</summary>
    public List<StartData.Slot> Slots { get; }

    /// <summary>해적 함대(60 ~ 69) — 나라 6.</summary>
    public bool Pirate => Index >= 60;

    /// <summary>나라(함대 번호 / 10).</summary>
    public int Nation => Index / 10;

    /// <summary>제독 사람 번호 — 함대 +0x2A.</summary>
    public int Admiral { get; }
    public int Heading { get; private set; }
    public double X => _cellX + _subX / (double)Sailing.CellUnits;
    public double Y => _cellY + _subY / (double)Sailing.CellUnits;

    /// <summary>항구에 머무는 남은 날. 0 이 아니면 지도에 안 그린다.</summary>
    public int DaysInPort { get; private set; }

    /// <summary>항구에 들어 있는지.</summary>
    public bool Docked => DaysInPort > 0;

    /// <summary>항구에 든 채로 시작한다 — 그만큼 머문 뒤 할 일을 고른다.</summary>
    public void StayInPort(int days)
    {
        _route.Clear();
        _leg = 0;
        DaysInPort = Math.Max(1, days);
    }

    /// <summary>
    /// 떠날 때 뭍(항구) 위면 둘레 바다 칸으로 옮긴다(<c>0x0043B230</c> 이 항구 둘레 열두 자리를 찾는다).
    /// </summary>
    private void PutToSea()
    {
        if (_sea.CanSail(_cellX, _cellY)) return;
        for (int r = 1; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && _sea.CanSail(_cellX + dx, _cellY + dy))
                    {
                        _cellX += dx; _cellY += dy;
                        return;
                    }
    }

    private void Plan((int X, int Y) target)
    {
        PutToSea();
        _route.Clear();
        _leg = 0;
        var nodes = _graph.Path(_graph.Nearest(_cellX, _cellY), _graph.Nearest(target.X, target.Y));
        foreach (int n in nodes) _route.Add((_graph.Nodes[n].X, _graph.Nodes[n].Y));
        _route.Add(target);
        _newLeg = true;
    }

    /// <summary>날 넘김 — 항구에 있으면 하루를 줄이고(<c>0x00402510</c>), 다 머물렀으면 다음 일을 고른다.</summary>
    public void NewDay()
    {
        if (DaysInPort == 0 || --DaysInPort > 0) return;
        if (World == null)
        {
            _towardHome = !_towardHome;
            Plan(_towardHome ? _home : _away);
            return;
        }
        PortStay();
    }

    // ── 할 일(움직임 갈래 +0x1B) ─────────────────────────────────────────────

    /// <summary>남 함대가 들여다보는 바깥 — 항구 자리, 나라 표, 주인공 함대, 다른 함대.</summary>
    public interface IWorld
    {
        (int X, int Y)? PortPos(int port);

        /// <summary>나라 표(<c>0x0043AD80</c>, 32 바이트) — +0 세기(낱말) · +2 노리는 나라(7 이면 주인공) · +3 방침 · +4 상선 행선 항구 · +0x0A 본거지 항구.</summary>
        int Nation(int nation, int field);

        /// <summary>나라 세기(+0 낱말).</summary>
        int NationPower(int nation);

        /// <summary>항구 형편 — 남 함대의 교역 · 투자 · 사들이기가 바꾼다.</summary>
        Support.Local.Formats.Economy? Economy { get; }

        int HeroRank { get; }
        int HeroNation { get; }
        int HeroFleetIndex { get; }
        int HeroAdmiral { get; }
        bool HeroAtSea { get; }
        (int X, int Y) HeroCell { get; }
        NpcFleet? FleetByIndex(int index);
    }

    public IWorld? World { get; set; }

    /// <summary>
    /// 움직임 갈래(+0x1B) — 0 고향으로 · 1 투자 · 2 사들이기 · 3 바다에 머묾 · 4 무차별(제 함대와 맞닿으면 친다) · 5 · 6 노림 ·
    /// 7 숨어 기다림 · 8 호위 · 9 고향으로. 4 ~ 8 은 <see cref="Goal"/> 의 사람을 따라간다.
    /// </summary>
    public int Mode { get; set; }

    /// <summary>+0x1C — 갈래 0 ~ 2 · 9 는 항구 번호, 4 ~ 8 은 따라갈 사람.</summary>
    public int Goal { get; set; }

    /// <summary>돈(+0x24 낱말) — 노획 금화가 (이것 + 1) x 100 이다.</summary>
    public int Gold { get; set; }

    /// <summary>아이템 넷(+0x1D ~ +0x20, 0xFF 빈 칸) — 노획 아이템은 첫 칸.</summary>
    public int[] Items { get; } = [0xFF, 0xFF, 0xFF, 0xFF];

    /// <summary>짐(+0x21 교역품, 0xFF 없음 → -1 · +0x22 양).</summary>
    public int CargoGood { get; set; } = -1;
    public int CargoAmount { get; set; }

    /// <summary>고향 항구(+0x26).</summary>
    public int HomePort => Record.Port;

    /// <summary>
    /// 항구에서 다 머물렀을 때(<c>0x004028A0</c>, 손표 <c>0x00454108</c>) — 갈래 0 은 교역을 마치고 방침으로 다음 갈래를 고르고
    /// (<c>0x00402180</c> → <c>0x004024A0</c>), 1 투자 · 2 사들이기(<c>0x00402530</c> · <c>0x00402750</c>)는 일을 마치면 갈래 0 으로 돌아간다.
    /// 그 뒤 갈래대로 행선을 잡는다(<c>0x004022E0</c>). 투자 · 사들이기의 항구 경제 몫은 아직 안 옮겼다.
    /// </summary>
    private void PortStay()
    {
        var w = World!;
        bool capital = w.Nation(Nation, 0x0A) == Goal;
        switch (Mode)
        {
            case 0 or 9:
                Trade(w);
                Mode = PolicyMode();
                break;
            case 1:
                if (Gold >= 2 && !capital && w.Economy is { } e && !Pirate) Gold = e.NpcInvest(Goal, Nation, Gold);
                Mode = 0;
                break;
            case 2:
                if (!capital && w.Economy is { } eb)
                {
                    int good = _rng.Next(16) + 10;
                    if (eb.NpcBuy(Goal, good, Gold, _rng.Next) is var (amount, cost)) { Gold -= cost; CargoGood = good; CargoAmount = amount; }
                }
                Mode = 0;
                break;
        }
        Destination();
    }

    /// <summary>
    /// 교역(<c>0x00402180</c>) — 짐이 있으면 그 항구에 팔고(<see cref="Support.Local.Formats.Economy.NpcSell"/>), 배 내구를 최대로 고치고,
    /// 빈 아이템 칸을 둘에 하나 꼴로 rand(20) + 50 으로 채우고(넷까지, 처음 빈 칸이 아니면 그대로), 돈 = 나라 세기 / 10 + rand(3) + 1.
    /// </summary>
    private void Trade(IWorld w)
    {
        if (CargoGood >= 0) w.Economy?.NpcSell(Goal, CargoGood, _rng.Next);
        for (int i = 0; i < Slots.Count; i++) Slots[i] = Slots[i] with { Durability = Slots[i].MaxDurability };
        for (int i = 0; i < Items.Length && _rng.Next(2) == 0; i++)
            if (Items[i] == 0xFF) Items[i] = _rng.Next(20) + 50;
        Gold = (short)w.NationPower(Nation) / 10 + _rng.Next(3) + 1;
        CargoGood = -1;
        CargoAmount = 0;
    }

    /// <summary>
    /// 방침으로 갈래 고르기(<c>0x004024A0</c>) — 머문 항구가 그 나라 본거지(나라 +0x0A)일 때만, 표 <c>0x004540F0</c>[나라 +3] 의
    /// 네 비트 하나. 상선(번호 % 10 이 1 ~ 4)은 비트 8 ~ 11, 7 ~ 9 는 0 ~ 3, 그 밖(0 · 5 · 6)은 4 ~ 7. 본거지가 아니면 0.
    /// </summary>
    public int PolicyMode()
    {
        if (World is not { } w || w.Nation(Nation, 0x0A) != Goal) return 0;
        return PolicyMode(w.Nation(Nation, 3), Index % 10);
    }

    /// <summary>방침 표(<c>0x004540F0</c>)의 갈래 — 방침 0 ~ 8, 함대 번호 % 10.</summary>
    public static int PolicyMode(int policy, int kind)
    {
        ReadOnlySpan<ushort> table = [0x198, 0x198, 0x298, 0x166, 0x156, 0x156, 0x156, 0x696, 0x646];
        if (policy < 0 || policy >= table.Length) return 0;
        int shift = kind is >= 1 and <= 4 ? 8 : kind >= 7 ? 0 : 4;
        return table[policy] >> shift & 0xF;
    }

    /// <summary>
    /// 행선(<c>0x004022E0</c>, 손표 <c>0x0040245C</c>) — 노리는 나라 tn 은 나라 +2(7 이면 주인공 나라).
    /// <code>
    ///   0 · 9  고향(+0x26)                 1 · 2  나라 +4 의 상선 행선 항구          3 · 7  그대로
    ///   4      주인공 함대를 쫓는다         8      제 나라 상선(번호 rand(4) + 1)을 지킨다
    ///   5 · 6  방침(나라 +3)대로 — 3 · 7 주인공, 4 · 8 tn 나라 함대(작위 2 넘으면 rand(5), 아니면 rand(4) + 1),
    ///          5 tn 나라 해군 rand(2) + 5, 6 해적 61 + rand(9)
    /// </code>
    /// </summary>
    private void Destination()
    {
        var w = World!;
        int tn = w.Nation(Nation, 2);
        if (tn == 7) tn = w.HeroNation;
        switch (Mode)
        {
            case 0 or 9: GoTo(HomePort); break;
            case 1 or 2: GoTo(w.Nation(Nation, 4)); break;
            case 4: Pursue(w.HeroFleetIndex); break;
            case 5 or 6:
                switch (w.Nation(Nation, 3))
                {
                    case 3 or 7: Pursue(w.HeroFleetIndex); break;
                    case 4 or 8: Pursue(w.HeroRank > 2 ? _rng.Next(5) + tn * 10 : _rng.Next(4) + tn * 10 + 1); break;
                    case 5: Pursue(_rng.Next(2) + 5 + tn * 10); break;
                    case 6: Pursue(61 + _rng.Next(9)); break;
                    default: GoTo(HomePort); break;
                }
                break;
            case 8: Pursue(_rng.Next(4) + 1 + Nation * 10); break;
            default: DaysInPort = _rng.Next(8) + 8; break;
        }
    }

    /// <summary>항구로(<c>0x00401F30</c>) — 목표 자리를 그 항구로 두고 떠난다.</summary>
    private void GoTo(int port)
    {
        Goal = port;
        if (World?.PortPos(port) is { } at) Plan(at);
        else DaysInPort = 5;
    }

    /// <summary>
    /// 함대를 쫓는다(<c>0x00402000</c>) — 그 함대가 바다에 있고 제독이 있으면 그 제독을 따라갈 사람으로, 아니면 고향으로.
    /// </summary>
    private void Pursue(int fleetIndex)
    {
        var w = World!;
        if (fleetIndex == w.HeroFleetIndex)
        {
            if (!w.HeroAtSea) { Mode = 0; GoTo(HomePort); return; }
            Goal = w.HeroAdmiral;
            Plan(w.HeroCell);
            return;
        }
        if (w.FleetByIndex(fleetIndex) is { Docked: false } f && f != this)
        {
            Goal = f.Admiral;
            Plan(((int)f.X, (int)f.Y));
            return;
        }
        Mode = 0;
        GoTo(HomePort);
    }

    /// <summary>따라가는 사람의 지금 자리 — 닿을 때마다 다시 잡는다(<c>0x00418379</c>, 갈래 5 ~ 8 · 10).</summary>
    private bool Retarget()
    {
        if (World is not { } w || Mode is not (>= 5 and <= 8 or 10)) return false;
        if (Goal == w.HeroAdmiral)
        {
            if (!w.HeroAtSea) return false;
            Plan(w.HeroCell);
            return true;
        }
        for (int i = 0; i < 70; i++)
            if (w.FleetByIndex(i) is { Docked: false } f && f.Admiral == Goal && f != this)
            {
                Plan(((int)f.X, (int)f.Y));
                return true;
            }
        return false;
    }

    /// <summary>
    /// 제 함대와 맞닿았는지(<c>0x004189F0</c>) — 갈래 4 는 같은 칩에 서면 갈래 5 로 바꾸고 주인공을 노린다. 5 ~ 7 은 주인공을 따라가다
    /// 같은 칩에 서면. 그다음 <c>0x00418550</c> 이 낮(18 ~ 53 틱)에만, 주인공에게 작위가 있거나 이 함대가 덤비는 함대(+0x29 의 0x40)면 덮친다 —
    /// 아니면 고향으로 돌아간다.
    /// </summary>
    public bool MeetsHero(int timeOfDay)
    {
        if (World is not { HeroAtSea: true } w || Docked || Mode is < 4 or > 7) return false;
        if ((_cellX, _cellY) != w.HeroCell) return false;
        if (Mode == 4) { Mode = 5; Goal = w.HeroAdmiral; }
        else if (Goal != w.HeroAdmiral) return false;
        if (timeOfDay is < 18 or >= 54) return false;
        if (w.HeroRank == 0 && (Record.Flags & 0x40) == 0) { Mode = 0; GoTo(HomePort); return false; }
        return true;
    }

    /// <summary>싸움 뒤 — 고향으로 돌아간다(<c>0x00418672</c>: 셋에 하나).</summary>
    public void AfterBattle()
    {
        if (_rng.Next(3) == 0) { Mode = 0; GoTo(HomePort); }
    }

    /// <summary>한 틱. 바람 · 해류는 제 함대 자리의 것이다.</summary>
    public void Step((int Dir, int Speed) wind, (int Dir, int Speed) current)
    {
        if (Docked || _leg >= _route.Count) return;

        var goal = _route[_leg];
        int dx = goal.X - _cellX;
        if (dx > ChipMap.Width / 2) dx -= ChipMap.Width;
        if (dx < -ChipMap.Width / 2) dx += ChipMap.Width;
        int dy = goal.Y - _cellY;
        // 원본은 자리가 같아야 닿는다(0x0043B1B0). 우리 바다 셈은 뭍 칸(항구)에 못 서므로 한 칸 곁이면 닿은 것으로 치고,
        // 이틀을 한 칩도 못 나가면(뭍에 막혔으면) 그 마디를 건너뛴다.
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 1 || _stall >= StallTicks)
        {
            _stall = 0;
            if (++_leg >= _route.Count && !Retarget()) DaysInPort = _rng.Next(8) + 8;   // 0x004184B0 의 rand(8)+8
            _newLeg = true;
            return;
        }
        int steer = Steer(dx, dy);
        if (steer == 8) return;
        Heading = steer;

        int speed = Speed(wind, current);
        int was = _cellX << 12 | _cellY;
        if (speed > 0)
            foreach (int dir in (int[])[Heading, Heading + 1, Heading - 1])
            {
                var (hx, hy) = WindCurTable.Vector(dir & 7);
                if (_sea.Advance(ref _cellX, ref _cellY, ref _subX, ref _subY, hx * speed, hy * speed)) break;
            }
        _stall = (_cellX << 12 | _cellY) == was ? _stall + 1 : 0;
    }

    /// <summary>한 칩도 못 나간 틱 수 — <see cref="StallTicks"/> 가 되면 막힌 것으로 친다.</summary>
    private int _stall;
    private const int StallTicks = Fleet.TicksPerDay * 2;

    private int Speed((int Dir, int Speed) wind, (int Dir, int Speed) current)
    {
        if (_ships.Length == 0 || wind.Speed == 0) return 0;
        int angle = Sailing.AngleOf(wind.Dir, Heading);
        int slowest = 0xFF;
        foreach (var ship in _ships)
            slowest = Math.Min(slowest, Sailing.AdmiralShipSpeed(ship.SailKind, ship.Sail, ship.Upwind, angle,
                                                                 wind.Speed, _navLevel, _seamanship));
        int speed = Math.Max(Sailing.MinSpeed, slowest);

        int c = Sailing.AngleOf(current.Dir, Heading);
        if (c is >= 3 and <= 5) speed -= Math.Min(speed - 1, current.Speed);
        else if (c is < 2 or 7) speed += Math.Min(Sailing.MaxSpeed - speed, current.Speed);
        return speed;
    }

    /// <summary>
    /// 그 마디 쪽으로 한 걸음 방위 — <c>0x00448380</c> 의 선 긋기. 8 이면 제자리다.
    /// </summary>
    private int Steer(int dx, int dy)
    {
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        if (_newLeg) { _err = -(adx / 2); _newLeg = false; }
        int sx, sy;
        if (adx > ady)
        {
            sx = Math.Sign(dx);
            _err += ady;
            if (_err >= 0) { _err -= adx; sy = Math.Sign(dy); } else sy = 0;
        }
        else
        {
            sy = Math.Sign(dy);
            _err += adx;
            if (_err >= 0) { _err -= ady; sx = Math.Sign(dx); } else sx = 0;
        }
        return DirTable[(sy + 1) * 3 + sx + 1];
    }

    /// <summary>부호 (dy, dx) → 방위 — <c>0x00461FF8</c>. 가운데 8 은 제자리다.</summary>
    private static ReadOnlySpan<byte> DirTable => [7, 0, 1, 6, 8, 2, 5, 4, 3];
}
