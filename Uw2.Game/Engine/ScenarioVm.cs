using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine;

/// <summary>
/// 시나리오 기계 — 건물에 들 때 그 항구 · 건물의 이벤트를 찾아 돌린다(<c>KOUKAI2.EXE 0x0043F8C0</c> · <c>0x0043F750</c>).
/// </summary>
/// <remarks>
/// <code>
///   장(0x004702A4)   SNRn.DAT 머리 0x10 + 장 x 4 의 빅엔디언 dword 가 장 자리 E
///   절(0x004702A8)   E + 절 x 2 의 낱말만큼 민 자리 S 에서 0x4000 바이트를 담개에 올린다(0x0043E5A0)
///   방아쇠 표        S 맨 앞, [항구][건물][자리 hh ll] 넷씩, 항구 0xFF 로 끝 (0x0043F937)
///                    항구 0xA3 은 「0x82 아래 아무 항구」, 건물 0xFF 는 「아무 건물」
///   뛰기 · 갈래 자리  모두 S 에서 잰다
/// </code>
/// 옮긴 명령: 넣기 · 셈(00), 견주어 뛰기(10), 뛰기 FE, 대사 C8 · D4, 배 주기 F9, 돈 E4 · E6 · E7, 그침 F2 · F4 · FF,
/// 다음 절 F0 · 다음 장 F1 — 이벤트가 끝난 뒤 넘긴다(<c>0x0043F7BA</c>). 다음 장은 절을 0 으로, 깃발 앞 서른둘을 지운다.
/// <code>
///   C0 n       대사 창 고르기(0x0043D0E0) — 창 표 0x0045E348 의 n 번(1 위 · 2 아래 · 0 · 3 넓은 창)
///   CC hh ll   그 창에 얼굴(0x0043D4B0 → 0x00434E70), CD v 는 변수 v 의 얼굴
///   C9 v hh ll 고르기(0x0043D2B0) — 글 하나를 줄 바꿈(0x0A)으로 갈라 항목으로 띄우고 고른 차례(0 부터)를 변수 v 에.
///              물리기는 없다(0x00437FD0 은 0xFF 면 다시 묻는다)
///   D0 · DC    게임 기억 가리키개(0x0043D510) — 아직 옮기지 않는다
/// </code>
/// 나머지는 길이(<see cref="Scenario.LengthAt"/>)만큼 넘긴다.
/// 게임 기억을 가리키는 변수(갈래 1, <c>0x0049F260</c> 표)는 0 으로 읽는다.
/// </remarks>
public sealed class ScenarioVm
{
    public const int MaxSteps = 5000;

    private readonly byte[] _code;
    private readonly ScenarioText _text;
    private readonly ushort[] _vars = new ushort[256];
    private readonly bool[] _flags = new bool[2048];

    public ScenarioVm(byte[] code, ScenarioText text) { _code = code; _text = text; }

    /// <summary>장 · 절 — 새 게임은 0 · 0.</summary>
    public int Chapter { get; set; }
    public int Section { get; set; }

    /// <summary>대사 한 줄 — 창(C0)과 얼굴(CC, 없으면 -1)을 함께 든다.</summary>
    public readonly record struct Line(string Speaker, string Body, int Window = 0, int Face = -1);

    /// <summary>
    /// 고르기를 묻는다 — 아직 보이지 않은 대사(<paramref name="before"/>)를 먼저 보이고 항목 중 하나의 차례를 돌려준다.
    /// </summary>
    public delegate int Chooser(IReadOnlyList<Line> before, IReadOnlyList<string> options);

    /// <summary>한 번 돌린 것 — 대사와 돌린 결과.</summary>
    public sealed class Run
    {
        public List<Line> Lines { get; } = [];
        public List<int> Ships { get; } = [];
        public int Gold { get; set; }

        /// <summary>고르기 앞에서 이미 보인 대사 수 — 나머지(<see cref="Pending"/>)를 끝에 보인다.</summary>
        public int Shown { get; set; }
        public IReadOnlyList<Line> Pending => Lines.Skip(Shown).ToList();
    }

    /// <summary>고르기를 물을 곳. 없으면 늘 첫 항목.</summary>
    public Chooser? Choose { get; set; }

    private int Word(int at) => at + 1 < _code.Length ? _code[at] << 8 | _code[at + 1] : 0;

    private int SectionStart()
    {
        int o = 0x10 + Chapter * 4;
        if (o + 4 > _code.Length) return -1;
        int e = _code[o] << 24 | _code[o + 1] << 16 | _code[o + 2] << 8 | _code[o + 3];
        int s = e + Word(e + Section * 2);
        return e >= 0 && s < _code.Length ? s : -1;
    }

    /// <summary>
    /// 그 항구 · 건물에 걸린 이벤트가 있으면 돌린다. 없으면 null. <paramref name="gold"/> 는 지금 돈이고 결과의 Gold 가 새 돈이다.
    /// </summary>
    public Run? Enter(int port, int building, int gold, string name, string family)
    {
        int s = SectionStart();
        if (s < 0) return null;
        for (int t = s; t + 4 <= _code.Length; t += 4)
        {
            int p = _code[t], b = _code[t + 1];
            if (p == 0xFF) break;
            bool portOk = p == port || (p == 0xA3 && port < 0x82);
            bool bldOk = b == building || b == 0xFF;
            if (portOk && bldOk) return Execute(s, s + Word(t + 2), gold, name, family);
        }
        return null;
    }

    private int ValueOf(int kind, int x, bool immediateWord, int at) => kind switch
    {
        0 => _vars[x & 0xFF],
        2 => _flags[x] ? 1 : 0,
        3 => immediateWord ? Word(at) : x,
        _ => 0,                                   // 게임 기억 — 아직 0
    };

    private Run Execute(int baseAt, int ip, int gold, string name, string family)
    {
        bool nextSection = false, nextChapter = false;
        var run = Run_(baseAt, ip, gold, name, family, ref nextSection, ref nextChapter);
        if (nextSection) Section++;
        if (nextChapter)
        {
            Chapter++;
            Section = 0;
            Array.Clear(_flags, 0, 32);
        }
        return run;
    }

    private Run Run_(int baseAt, int ip, int gold, string name, string family, ref bool nextSection, ref bool nextChapter)
    {
        var run = new Run { Gold = gold };
        int window = 0, face = -1;
        for (int step = 0; step < MaxSteps && ip >= 0 && ip < _code.Length; step++)
        {
            byte op = _code[ip];
            int len = Scenario.LengthAt(_code, ip);
            if (len <= 0) { ip++; continue; }
            if (ip + len > _code.Length) break;
            int a1 = len > 1 ? _code[ip + 1] : 0, a2 = len > 2 ? _code[ip + 2] : 0;

            switch (op >> 6)
            {
                case 0:
                {
                    int dest = (op >> 4) & 3, src = (op >> 2) & 3, calc = op & 3;
                    int v = ValueOf(src, a2, len == 4, ip + 2);
                    if (calc == 3) break;
                    if (dest == 0)
                    {
                        int cur = _vars[a1];
                        _vars[a1] = (ushort)(calc == 0 ? v : calc == 1 ? cur + v : Math.Max(0, cur - v));
                    }
                    else if (dest == 2 && a1 < _flags.Length) _flags[a1] = calc == 2 ? false : v != 0;
                    break;
                }
                case 1:
                    break;
                case 2:
                {
                    int ka = (op >> 4) & 3, kb = (op >> 2) & 3, c = op & 3;
                    bool ok;
                    int target;
                    if (ka == 2 && kb != 2)
                    {
                        ok = (a1 < _flags.Length && _flags[a1]) == ((c & 1) == 1);
                        target = Word(ip + 2);
                    }
                    else
                    {
                        int va = ValueOf(ka, a1, false, 0), vb = ValueOf(kb, a2, false, 0);
                        ok = c switch { 0 => vb == va, 1 => vb != va, 2 => vb >= va, _ => va >= vb };
                        target = Word(ip + 3);
                    }
                    if (!ok) { ip = baseAt + target; continue; }
                    break;
                }
                default:
                    switch (op)
                    {
                        case 0xC8 or 0xD4:
                            var (who, body) = ScenarioText.Render(_text[Word(ip + 1)], name, family);
                            run.Lines.Add(new Line(who, body, window, face));
                            break;
                        case 0xC0:
                            if (a1 < 4 && a1 != window) { window = a1; face = -1; }
                            break;
                        case 0xCC: face = Word(ip + 1); break;
                        case 0xCD: face = _vars[a1]; break;
                        case 0xC9:
                        {
                            var options = ScenarioText.Render(_text[Word(ip + 2)], name, family).Body
                                .Split('\n', '\r').Where(o => o.Length > 0).ToList();
                            int pick = 0;
                            if (Choose != null && options.Count > 0)
                            {
                                pick = Math.Clamp(Choose(run.Pending, options), 0, options.Count - 1);
                                run.Shown = run.Lines.Count;
                            }
                            _vars[a1] = (ushort)pick;
                            break;
                        }
                        case 0xF9:
                            run.Ships.Add(a1);
                            break;
                        case 0xE4: run.Gold = _vars[a1]; break;
                        case 0xE6: run.Gold = Math.Min(PlayerState.MaxGold, run.Gold + _vars[a1]); break;
                        case 0xE7: run.Gold = Math.Max(0, run.Gold - _vars[a1]); break;
                        case 0xFE:
                            ip = baseAt + Word(ip + 1);
                            continue;
                        case 0xF0: nextSection = true; break;
                        case 0xF1: nextChapter = true; break;
                        case 0xF2 or 0xF4 or 0xFF:
                            return run;
                    }
                    break;
            }
            ip += len;
        }
        return run;
    }
}
