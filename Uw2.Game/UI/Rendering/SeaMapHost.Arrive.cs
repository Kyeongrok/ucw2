using Uw2.Game.Engine;
using Uw2.Game.Engine.Town;

namespace Uw2.Game.UI.Rendering;

/// <summary>바다에서 항구에 든다 — 옆 항구 찾기(<c>0x0041A8D0</c>) · 기항 셈(<c>0x0041A560</c>).</summary>
public sealed partial class SeaMapHost
{
    /// <summary>
    /// 함대 바로 옆의 항구 번호(<c>0x0041A8D0</c> → <c>0x0041A450</c>), 없으면 -1. 원본은 함대 둘레 열두 칸(<c>0x00458208</c>, 거리 2 의 고리)에
    /// 항구 칩(0x74 · 0x78)이 있으면 그 자리의 항구다.
    /// </summary>
    public int PortBeside()
    {
        if (Fleet == null || Ports is not { } pt || CurrentPort >= 0) return -1;
        int fx = (int)Math.Floor(Fleet.X), fy = (int)Math.Floor(Fleet.Y);
        // 원본 자리는 배 그림 왼위라 고리만 본다. 우리 자리는 배 한가운데라 고리 안쪽까지(5 x 5) 본다.
        for (int p = 0; p < pt.Ports.Count; p++)
            if (Math.Abs(pt.Ports[p].X - fx) <= 2 && Math.Abs(pt.Ports[p].Y - fy) <= 2) return p;
        return -1;
    }

    /// <summary>
    /// 기항(<c>0x0041A560</c>) — 항구 표 +0x15 에 0x50(들른 항구)을 세우고, 바다에 나와 있던 날 수 d 로 항해 경험을 준다:
    /// 주인공과 배를 맡은 동료(처지 2 · 3)는 min(d, 70)² x 2, 나머지 동료는 min(d, 100)². 레벨이 오른 이마다 알릴 글을 돌려준다
    /// (<c>0x0041A7C0</c> — 「%s의 항해레벨이 올라갔습니다」, 오른 능력치 이름 뒤 「이 올라갔습니다」). 그 다음 항구를 띄운다.
    /// </summary>
    public List<string> Arrive(int port)
    {
        var notes = new List<string>();
        if (Fleet == null) return notes;
        int days = Fleet.DaysAtSea;
        if (Scenario is { } vm)
        {
            var flags = ScenarioMemory.At(ScenarioMemory.PortTable, port, 0x15);
            vm.Memory.Write(flags, vm.Memory.Read(flags, false) | 0x50, false);
            int lead = Math.Min(days, 70) * Math.Min(days, 70) * 2, crew = Math.Min(days, 100) * Math.Min(days, 100);
            string[] stats = ["통솔력", "항해술", "지식"];
            void Gain(int person, int amount, string who)
            {
                var (ups, raised) = Navigators.SailExperience(vm.Memory, person, amount, Roll);
                if (ups == 0) return;
                notes.Add($"{who}의 항해레벨이 올라갔습니다"
                          + (raised.Count > 0 ? $" — {string.Join(" ", raised.Select(k => stats[k]))} 이 올라갔습니다" : ""));
            }
            Gain(HeroPersonNumber, lead, "제독");
            foreach (var (_, person, _) in Navigators.Mates(vm.Memory).ToList())
            {
                int status = Navigators.Get(vm.Memory, person, Navigators.Status);
                Gain(person, status is 2 or 3 ? lead : crew, PersonName(person));
            }
        }
        Log.Add(Fleet.Date.Year, Fleet.Date.Month, Fleet.Date.Day, Logbook.Arrived, days, port);   // 0x0041AA1D
        Fleet.UnderSail = false;
        if (port >= SmallPortFirst) EnterSmallPort(port);
        else ShowPort(port);
        return notes;
    }

    /// <summary>항해일지(<see cref="Logbook"/>).</summary>
    public Logbook Log { get; } = new();

    /// <summary>100 부터는 작은 항구 — 도시를 걷지 않고 목록 57(출항 · 보급 · 명명)만 뜬다(<c>0x00423CBB</c> → <c>0x00424090</c>).</summary>
    public const int SmallPortFirst = 100;

    /// <summary>작은 항구에 든다 — 세계지도를 그대로 두고 항구 번호만 세운다.</summary>
    private void EnterSmallPort(int port)
    {
        CurrentPort = port;
        AtSea = false;
        Walker = null;
        Player.Rats = Player.Scurvy = false;
        _dirty = true;
    }

    /// <summary>
    /// 「명명」(<c>0x00423B90</c>) — 작은 항구에 애칭(여덟 바이트)을 붙인다. 같은 이름의 항구가 있으면 false(말 0x39F).
    /// 이름은 시나리오 기억의 항구 표(+4)에도 적어 세이브에 남는다.
    /// </summary>
    public bool RenamePort(int port, string name)
    {
        if (Ports is not { } pt || port < 0 || port >= pt.Ports.Count || name.Length == 0) return false;
        if (pt.Ports.Any(p => p.Index != port && p.Name == name)) return false;
        pt.Rename(port, name);
        if (Scenario is { } vm)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var bytes = System.Text.Encoding.GetEncoding(949).GetBytes(name);
            for (int i = 0; i < 17; i++)
                vm.Memory.Write(ScenarioMemory.At(ScenarioMemory.PortTable, port, 4 + i), i < bytes.Length && i < 16 ? bytes[i] : 0, false);
        }
        return true;
    }

    /// <summary>세이브를 들인 뒤 — 시나리오 기억의 작은 항구 이름(명명한 것)을 항구 표로 되돌린다.</summary>
    private void SyncPortNames()
    {
        if (Ports is not { } pt || Scenario is not { } vm) return;
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cp949 = System.Text.Encoding.GetEncoding(949);
        for (int p = SmallPortFirst; p < pt.Ports.Count; p++)
        {
            var raw = Enumerable.Range(0, 16).Select(i => (byte)vm.Memory.Read(ScenarioMemory.At(ScenarioMemory.PortTable, p, 4 + i), false)).TakeWhile(b => b != 0).ToArray();
            if (raw.Length > 0) pt.Rename(p, cp949.GetString(raw));
        }
    }

    /// <summary>
    /// 이 항구에 남고 싶은 동료(<c>0x00423DC1</c>) — 배를 안 맡은 동료 가운데 호감이 30 아래이고 rand(4 − (+0x2F &amp; 3)) == 0 이며
    /// 급여가 190 아래인 이들. (사람, 급여)
    /// </summary>
    public List<(int Person, int Wage)> MatesWantingToStay()
    {
        var list = new List<(int, int)>();
        if (Scenario is not { } vm) return list;
        foreach (var (_, person, wage) in Navigators.Mates(vm.Memory).ToList())
        {
            if (Navigators.Get(vm.Memory, person, Navigators.Status) <= Navigators.Captain) continue;
            if (Navigators.Get(vm.Memory, person, Navigators.Liking) >= 30) continue;
            if (Roll(4 - (Navigators.Get(vm.Memory, person, Navigators.Mood) & 3)) != 0) continue;
            if (wage / 10 >= 0x13) continue;
            list.Add((person, wage));
        }
        return list;
    }

    private int _portAsked = -1;

    /// <summary>항구 옆에서 막혔다 — 들지 물을 항구 번호를 넘긴다. 같은 항구는 거기서 떠날 때까지 한 번만 묻는다.</summary>
    public event Action<int>? PortAhead;

    /// <summary>바다 틱마다 — 뭍에 막혀 섰는데 옆이 항구면 묻는다(원본은 그 항구를 누르거나 자동 항해가 닿으면 묻는다, <c>0x0041AEE0</c> · <c>0x00448B5B</c>).</summary>
    private bool CheckPortAhead()
    {
        if (Fleet is not { } f) return false;
        int p = PortBeside();
        if (p < 0) { _portAsked = -1; return false; }
        if (!f.Blocked || p == _portAsked) return false;
        _portAsked = p;
        f.UnderSail = false;
        Dispatcher.BeginInvoke(() => PortAhead?.Invoke(p));
        return true;
    }

    /// <summary>남겠다는 동료를 붙든다 — 급여 +10, 호감 30(<c>0x00423EC1</c>).</summary>
    public void KeepMate(int person)
    {
        if (Scenario is not { } vm) return;
        foreach (var (slot, p, wage) in Navigators.Mates(vm.Memory))
            if (p == person)
            {
                vm.Memory.Write(new ScenarioMemory.Pointer(Navigators.MateTable, Navigators.WageFirst + slot), wage / 10 + 1, false);
                Navigators.Set(vm.Memory, person, Navigators.Liking, 30);
                return;
            }
    }

    /// <summary>동료가 이 항구에 남는다(<c>0x00424030</c>) — 동료 칸 · 급여를 비우고 처지 0, 함대 없음, 머문 항구 = 여기.</summary>
    public void MateStays(int person)
    {
        if (Scenario is not { } vm || CurrentPort < 0) return;
        foreach (var (slot, p, _) in Navigators.Mates(vm.Memory).ToList())
            if (p == person)
            {
                vm.Memory.Write(new ScenarioMemory.Pointer(Navigators.MateTable, Navigators.MateFirst + slot), 0xFF, false);
                vm.Memory.Write(new ScenarioMemory.Pointer(Navigators.MateTable, Navigators.WageFirst + slot), 0, false);
            }
        Navigators.Set(vm.Memory, person, Navigators.Status, 0);
        Navigators.Set(vm.Memory, person, Navigators.Fleet, 0xFF);
        Navigators.Set(vm.Memory, person, Navigators.Port, CurrentPort);
    }
}
