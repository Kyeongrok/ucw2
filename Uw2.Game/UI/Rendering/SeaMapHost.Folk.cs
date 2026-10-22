using Uw2.Game.Engine;
using Uw2.Game.Engine.Town;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Rendering;

/// <summary>도시의 마을 사람(<see cref="Townsfolk"/>) — 내놓기 · 걸음 · 부딪치면 말 걸기 · 그리기.</summary>
public sealed partial class SeaMapHost
{
    /// <summary>마을 사람 한 걸음 틱(초) — 우리 사람이 한 칸 가는 사이(<see cref="Walker.CellTicks"/> 틱).</summary>
    public const double FolkTickSeconds = Walker.CellTicks * Walker.TickSeconds;

    /// <summary>말을 건 뒤 다시 말을 걸 수 있는 걸음(칸) — <c>0x00459C68</c> 이 열이 되어야 한다.</summary>
    public const double FolkTalkSteps = 10;

    public Townsfolk Folk { get; } = new();

    /// <summary>마을 사람과 부딪쳤다 — 말 갈래(1 ~ 8)를 넘긴다(<c>0x004330AF</c> → <c>0x004335F0</c>).</summary>
    public event Action<int>? FolkMet;

    private double _folkCarry, _folkTalkFrom;
    private int _folkFrames = 8;

    /// <summary>항구에 들 때 문 곁에 세운다. 그 나라가 주인공 나라의 기항을 막으면 항 문에 경비병(<c>0x00436248</c>).</summary>
    private void SpawnFolk(int port)
    {
        var doors = _doors?.Of(port);
        if (doors == null) { Folk.Spawn([], false); return; }
        int nation = PortNation(port), hero = HeroNation;
        bool banned = Scenario is { } vm && nation < Economy.NoNation && hero < Economy.NoNation
                      && (vm.Memory.Read(ScenarioMemory.At(ScenarioMemory.NationTable, nation, 0x12 + hero), false) & 0x10) != 0;
        Folk.Spawn(doors, banned);
        _folkTalkFrom = (Walker?.Walked ?? 0) - FolkTalkSteps;
        _folkCarry = 0;
    }

    /// <summary>사람 그림 왼위 칸.</summary>
    private static (int X, int Y) TopLeft(Walker w) => ((int)Math.Round(w.X) - 1, (int)Math.Floor(w.Y) - 1);

    /// <summary>마을 사람을 걸린다 — 걷는 이 넷이 틱마다 옮긴다(<c>0x00432CEF</c>).</summary>
    private void AdvanceFolk(double dt)
    {
        if (Walker is not { } w) return;
        _folkCarry += dt;
        int n = 0;
        while (_folkCarry >= FolkTickSeconds && n++ < 4)
        {
            _folkCarry -= FolkTickSeconds;
            Folk.Step((x, y) => w.CanWalk(x + 1, y + 1.5), TopLeft(w), Roll);
            _folkFrame = null;
        }
        if (_folkCarry > FolkTickSeconds) _folkCarry = 0;
    }

    /// <summary>
    /// 우리 사람이 마을 사람에게 들어섰으면 되돌리고 참(<c>0x00433094</c>). 열 걸음 넘게 걸었으면 말을 건다.
    /// </summary>
    private bool BumpFolk(double fromX, double fromY)
    {
        if (Walker is not { } w) return false;
        var (x, y) = TopLeft(w);
        int who = Folk.At(x, y);
        if (who < 0) return false;
        w.Put(fromX, fromY);
        if (w.Walked - _folkTalkFrom >= FolkTalkSteps)
        {
            _folkTalkFrom = w.Walked;
            int kind = Folk.Kind(who);
            Dispatcher.BeginInvoke(() => FolkMet?.Invoke(kind));
        }
        return true;
    }

    private (float X, float Y, bool West, int Frame)[]? _folkFrame;

    /// <summary>마을 사람을 렌더러에 올린다 — 그림 왼위 칸이 (x, y) 라 밑변 한가운데는 (x + 1, y + 2) 다.</summary>
    private void PushFolk()
    {
        _folkFrame ??= [.. Enumerable.Range(0, Townsfolk.Count)
            .Where(i => Folk.People[i].Here && _folkFrames > 8 + Folk.Frame(i, _ => 0))
            .Select(i => ((float)(Folk.People[i].X + 1), (float)(Folk.People[i].Y + 2), false, 8 + Folk.Frame(i, Roll)))];
        float size = (float)(2.0 / CellsPerPixel);
        _renderer.SetOthers(_folkFrame, size, size);
        _othersScale = -1;   // 바다에 나가면 남 함대를 다시 올린다
    }

    private byte[] _heroWalkBase = [], _heroWalkers = [], _folkArt = [];
    private int _folkArtW;

    /// <summary>주인공 그림을 그 사람 번호 줄로 갈고(<c>0x00436106</c>) 마을 사람 판을 다시 붙인다.</summary>
    private void UseHeroWalker(int person)
    {
        if (_heroWalkBase.Length == 0) return;
        int w = _heroWalkBase.Length / Math.Max(1, _walkArtH), size = w * _walkArtH;
        _walkArt = _heroWalkers.Length >= (person + 1) * size && person >= 0
            ? _heroWalkers.AsSpan(person * size, size).ToArray() : _heroWalkBase;
        _walkArtW = w;
        JoinFolkSheet(_folkArt, _folkArtW);
        if (Walker != null) UseWalkerSprite(true);
    }

    /// <summary>사람 판 뒤에 마을 사람 판을 붙인다(높이가 같을 때만).</summary>
    private void JoinFolkSheet(byte[] folk, int folkW)
    {
        if (folk.Length == 0 || _walkArt.Length == 0 || folk.Length != folkW * _walkArtH) return;
        int w = _walkArtW + folkW;
        var px = new byte[w * _walkArtH];
        for (int y = 0; y < _walkArtH; y++)
        {
            Array.Copy(_walkArt, y * _walkArtW, px, y * w, _walkArtW);
            Array.Copy(folk, y * folkW, px, y * w + _walkArtW, folkW);
        }
        _walkArt = px;
        _walkArtW = w;
        _folkFrames = w / Walker.SpriteW;
    }

    /// <summary>
    /// 마을 사람 말(<c>0x004335F0</c>) — 갈래 1 은 0x282 + 항구, 2 는 0x276 + 날 % 10, 3 은 중고선(0x280 선종 · 없으면 0x281),
    /// 4 는 0x26F + 그 나라, 5 는 이 항구 소개(100 넘는 항구 0x242 · 서울 0x243 · 나라 항구 0x244(식민지 · 동맹항) · 그 밖 0x245),
    /// 6 은 개(0x26B + 항구 % 3), 7 은 경비병(0x26E 주인공 나라), 8 은 장사꾼(0x268 뒤 특산 0x269 · 없으면 0x26A).
    /// 말 번호와 끼울 것들을 돌려준다.
    /// </summary>
    public List<(int Id, object[] Args)> FolkLines(int kind, string[] nationNames)
    {
        var lines = new List<(int, object[])>();
        int port = CurrentPort;
        string portName = Ports is { } pt && port >= 0 && port < pt.Ports.Count ? pt.Ports[port].Name : "";
        int nation = PortNation(port);
        string N(int k) => k >= 0 && k < nationNames.Length ? nationNames[k] : "";
        switch (kind)
        {
            case 1: lines.Add((0x282 + port, [])); break;
            case 2: lines.Add((0x276 + (Fleet?.Date.Day ?? 0) % 10, [])); break;
            case 3:
            {
                var used = Scenario is { } vm && StartTables is { } st ? Shipyard.Used(vm.Memory, st.ShipTypes.Count) : [];
                if (used.Count > 0 && StartTables is { } s2 && used[0].Type < s2.Templates.Count)
                    lines.Add((0x280, [s2.Templates[used[0].Type].Name ?? ""]));
                else lines.Add((0x281, []));
                break;
            }
            case 4: lines.Add((0x26F + nation, [])); break;
            case 5:
            {
                bool capital = Scenario is { } vm && Enumerable.Range(0, 6)
                    .Any(k => vm.Memory.Read(ScenarioMemory.At(ScenarioMemory.NationTable, k, StartData.CapitalField), false) == port);
                int flags = Scenario?.Memory.Read(ScenarioMemory.At(ScenarioMemory.PortTable, port, 0x15), false) ?? 0;
                if (port >= 100) lines.Add((0x242, [portName]));
                else if (capital) lines.Add((0x243, [N(nation), portName]));
                else if (nation < Economy.NoNation) lines.Add((0x244, [N(nation), (flags & 0x80) != 0 ? "식민지" : "동맹항", portName]));
                else lines.Add((0x245, [portName]));
                break;
            }
            case 6: lines.Add((0x26B + port % 3, [])); break;
            case 7: lines.Add((0x26E, [N(HeroNation)])); break;
            default:
                lines.Add((0x268, []));
                int special = Economy is { } eco && port >= 0 && port < eco.States.Count ? eco.States[port].Special : -1;
                if (special >= 0 && Economy is { } e2 && special < e2.GoodsNames.Count) lines.Add((0x269, [e2.GoodsNames[special]]));
                else lines.Add((0x26A, []));
                break;
        }
        return lines;
    }
}
