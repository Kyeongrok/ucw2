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
/// 바이트 갈래(<c>0x0043E830 · 0x0043EB90 · 0x0043ED90</c>):
/// <code>
///   00 DD SS WW a b   넣기 — 갈래 S 의 b 를 갈래 D 의 a 에. W 0 낱말 · 1 바이트, 3 은 게임 값 b(<see cref="GameValues"/>), 2 는 오류.
///                     D 가 깃발이면 0 아님을 넣는다. 받을 곳이 깃발이 아니고 W 0 · S 3 이면 b 뒤 한 바이트를 더 읽어 낱말
///   01 O B SS oo a b  셈 — B 0 이면 변수 a(낱말), 1 이면 가리키개 a 자리(바이트)에. O·oo 0 더하기 1 빼기 2 곱 3 나누기,
///                     O 가 서면 0 또는 1 그리고 2 · 3 은 가리키개 a 를 b 만큼 앞 · 뒤로
///   10 AA BB cc a b t 견주기 — 낱말 A ? B (cc 0 같다 1 다르다 2 A ≥ B 3 B ≥ A), 틀리면 절 바탕 + t 로
/// </code>
/// 갈래: 0 변수(낱말) · 1 가리키개(<see cref="ScenarioMemory"/>) · 2 깃발(넣을 때는 0xFFFF) · 3 바로 값.
/// 옮긴 명령: 뛰기 FE, 대사 C8 · D4, 배 주기 F9, 돈 E4 · E6 · E7, 그침 F2 · F4 · FF,
/// 다음 절 F0 · 다음 장 F1 — 이벤트가 끝난 뒤 넘긴다(<c>0x0043F7BA</c>). 다음 장은 절을 0 으로, 깃발 앞 서른둘을 지운다.
/// <code>
///   C0 n       대사 창 고르기(0x0043D0E0) — 창 표 0x0045E348 의 n 번(1 위 · 2 아래 · 0 · 3 넓은 창)
///   CC hh ll   그 창에 얼굴(0x0043D4B0 → 0x00434E70), CD v 는 변수 v 의 얼굴
///   C9 v hh ll 고르기(0x0043D2B0) — 글 하나를 줄 바꿈(0x0A)으로 갈라 항목으로 띄우고 고른 차례(0 부터)를 변수 v 에.
///              물리기는 없다(0x00437FD0 은 0xFF 면 다시 묻는다)
///   D0 p t r f 가리키개 p 에 표 t 의 r 번 기록 f 자리(0x0043D510). D0 은 r 을 변수 r 에서, DC 는 그대로
///   E9 f       예 · 아니오를 물어 깃발 f 를 세우거나 지운다(0x0043DCE0)
///   EA v       변수 v = 돈 / 10000 (0x0043DD50)
///   EB v hh ll 변수 v = 주사위(hhll) — 씨앗 = 씨앗 x 0x5D588B65 + 1, (씨앗 &gt;&gt; 8 &amp; 0x7FFF) % hhll (0x0043DD80)
///   EC v · ED v  씨앗 = 변수 v &lt;&lt; 8 · 변수 v = 씨앗 &gt;&gt; 8 (0x0043DDE0 · 0x0043DE00)
///   EE v       변수 v = 우리 배들의 빈 짐 자리 합 (0x0043DE20)
///   E2 g a     변수 g 의 교역품을 변수 a 만큼 값 없이 싣는다 — 배마다 빈 자리까지, 넘치면 버린다(0x0043D980)
///   E3 g a     변수 g 의 교역품을 모든 배에서 내리고 내린 양을 변수 a 에(0x0043DAB0)
///   FB p       사람 p 가 동료로 든다 — 빈 동료 칸에, 처지 6, 함대 · 머무는 곳은 주인공 것(0x0043E0D0)
///   씨앗은 이벤트를 열 때 ((주인공 +0x26 낱말 + 항해 레벨) x 해 x 달 + 날 + 시각) &lt;&lt; 8 (0x0043F9F0)
/// </code>
/// 나머지는 길이(<see cref="Scenario.LengthAt"/>)만큼 넘긴다.
/// </remarks>
public sealed class ScenarioVm
{
    public const int MaxSteps = 5000;

    private readonly byte[] _code;
    private readonly ScenarioText _text;
    private readonly ushort[] _vars = new ushort[256];
    private readonly bool[] _flags = new bool[2048];
    private readonly ScenarioMemory.Pointer[] _ptrs = new ScenarioMemory.Pointer[256];

    public ScenarioVm(byte[] code, ScenarioText text, ScenarioMemory? memory = null)
    {
        _code = code; _text = text; Memory = memory ?? new ScenarioMemory();
    }

    /// <summary>
    /// 게임 값 여덟(<c>0x0043E8DA</c> 의 표 <c>0x0043EB60</c>) — <c>00 0x 11 a b</c>(W 3)가 변수 a 에 b 번을 넣는다. 부르는 쪽이 채운다.
    /// <code>
    ///   1  제 함대가 있는 바다 칸 — (y / 24) x 90 + x / 24 (칩 자리, 0x0046F8C8)
    ///   2  해(0 부터, 0x004703D8)   3  달(0x004703DC)   4  날(0x004703E0)
    ///   5  머문 항구(0x0045A1C0, 바다면 255)   6  0x004715AC(모름)   7  하루 안 시각(0x004703E4)
    /// </code>
    /// 0 은 받을 곳이 가리키개일 때 입력 글을 그 자리에 옮기는 것이라 여기서는 쓰지 않는다.
    /// </summary>
    public int[] GameValues { get; } = new int[8];

    /// <summary>
    /// 짐 넣고 빼기 — (교역품, 양) → 실제로 옮긴 양. 양이 <see cref="int.MinValue"/> 면 그 교역품을 다 내리고 내린 양을 돌려준다.
    /// E2(싣기) · E3(내리기)가 부른다. 부르는 쪽이 배 곁 기억(표 9)도 맞춰 둔다.
    /// </summary>
    public Func<int, int, int>? Cargo { get; set; }

    /// <summary>장이 바뀌면 다음 장 대신 장 0 으로 — 함께 쓰는 시나리오(SNR0, 조합 일)가 이렇다(<c>0x0043F889</c>).</summary>
    public bool EndsAtChapterZero { get; init; }

    /// <summary>우리 배의 빈 짐 자리 합 — EE 가 읽는다(배마다 배 기록 +0x14 − 짐). 부르는 쪽이 채운다.</summary>
    public int FreeRoom { get; set; }

    /// <summary>이벤트 주사위 씨앗(<c>0x0045E3AC</c>).</summary>
    private uint _seed;

    public const int ValueArea = 1, ValueYear = 2, ValueMonth = 3, ValueDay = 4, ValuePort = 5, ValueTime = 7;

    /// <summary>시나리오가 들여다보는 게임 기억 — 부르는 쪽이 아는 표를 채운다.</summary>
    public ScenarioMemory Memory { get; }

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

        /// <summary>동료로 든 사람(FB).</summary>
        public List<int> Joined { get; } = [];
        public int Gold { get; set; }

        /// <summary>
        /// 건물 볼일을 대신했는지 — <c>F8</c> 이 기계 고리의 값을 0 으로 만들면(<c>0x0043F53F</c>) 원본은 건물 목록을 띄우지 않는다
        /// (<c>0x00431FF9</c>). 그침 <c>F2</c> 는 고리만 멈추고 값은 1 로 둬 목록이 이어서 뜬다. <c>FF</c> 는 아무것도 아니다.
        /// </summary>
        public bool Stopped { get; set; }

        /// <summary>고르기 앞에서 이미 보인 대사 수 — 나머지(<see cref="Pending"/>)를 끝에 보인다.</summary>
        public int Shown { get; set; }
        public IReadOnlyList<Line> Pending => Lines.Skip(Shown).ToList();
    }

    /// <summary>
    /// 글 안의 기억 표시를 푼다(<c>0x0043E1D0</c>) — <c>$rNN</c> 은 가리키개 NN 자리의 글(0 까지, cp949),
    /// <c>$dNN</c> 은 변수 NN 의 십진수. <c>$n</c> · <c>$s</c> 는 <see cref="ScenarioText.Render"/> 가 푼다.
    /// </summary>
    public string Expand(string raw)
    {
        if (!raw.Contains('$')) return raw;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '$' && i + 3 < raw.Length && raw[i + 1] is 'r' or 'd' && char.IsDigit(raw[i + 2]) && char.IsDigit(raw[i + 3]))
            {
                int n = (raw[i + 2] - '0') * 10 + (raw[i + 3] - '0');
                sb.Append(raw[i + 1] == 'd' ? _vars[n].ToString() : ReadText(_ptrs[n]));
                i += 3;
                continue;
            }
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    private string ReadText(ScenarioMemory.Pointer p)
    {
        var bytes = new List<byte>();
        for (int k = 0; k < 32; k++)
        {
            int b = Memory.Read(p with { Offset = p.Offset + k }, false);
            if (b == 0) break;
            bytes.Add((byte)b);
        }
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(949).GetString([.. bytes]);
    }

    /// <summary>변수 하나 — 조합 「업무 알선」처럼 손이 시나리오 변수를 바로 짚을 때(<c>0x0041E9E2</c>).</summary>
    public int GetVar(int i) => _vars[i & 0xFF];
    public void SetVar(int i, int v) => _vars[i & 0xFF] = (ushort)v;
    public bool GetFlag(int i) => i >= 0 && i < _flags.Length && _flags[i];

    /// <summary>세이브에 담는 기계 상태 — 장 · 절 · 변수 · 깃발 · 가리키개 · 기억.</summary>
    public sealed class State
    {
        public int Chapter { get; set; }
        public int Section { get; set; }
        public ushort[] Vars { get; set; } = [];
        public bool[] Flags { get; set; } = [];
        public int[] Pointers { get; set; } = [];
        public byte[][] Memory { get; set; } = [];
    }

    public State Export() => new()
    {
        Chapter = Chapter, Section = Section, Vars = [.. _vars], Flags = [.. _flags],
        Pointers = [.. _ptrs.SelectMany(p => (int[])[p.Table, p.Offset])], Memory = Memory.Export(),
    };

    public void Import(State s)
    {
        Chapter = s.Chapter;
        Section = s.Section;
        s.Vars.AsSpan(0, Math.Min(s.Vars.Length, _vars.Length)).CopyTo(_vars);
        s.Flags.AsSpan(0, Math.Min(s.Flags.Length, _flags.Length)).CopyTo(_flags);
        for (int i = 0; i < _ptrs.Length && 2 * i + 1 < s.Pointers.Length; i++)
            _ptrs[i] = new ScenarioMemory.Pointer(s.Pointers[2 * i], s.Pointers[2 * i + 1]);
        Memory.Import(s.Memory);
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

    /// <summary>
    /// 갈래 값 읽기(<c>0x0043E6E0</c>) — <paramref name="word"/> 가 아니면 낮은 바이트만. 깃발은 0xFFFF · 0.
    /// </summary>
    private int ValueOf(int kind, int x, bool word = true) => kind switch
    {
        0 => word ? _vars[x & 0xFF] : _vars[x & 0xFF] & 0xFF,
        1 => Memory.Read(_ptrs[x & 0xFF], word),
        2 => x < _flags.Length && _flags[x] ? (word ? 0xFFFF : 0xFF) : 0,
        _ => x,
    };

    /// <summary>받을 곳에 쓰기(<c>0x0043E7B0</c>) — 변수는 낱말, 가리키개는 <paramref name="word"/> 대로.</summary>
    private void Store(int kind, int x, int v, bool word)
    {
        if (kind == 0) _vars[x & 0xFF] = (ushort)(word ? v : v & 0xFF);
        else if (kind == 1) Memory.Write(_ptrs[x & 0xFF], v, word);
        else if (kind == 2 && x < _flags.Length) _flags[x] = v != 0;
    }

    private Run Execute(int baseAt, int ip, int gold, string name, string family)
    {
        int hero = Memory.Read(new ScenarioMemory.Pointer(ScenarioMemory.GoldTable, ScenarioMemory.HeroField), false);
        int w26 = Memory.Read(ScenarioMemory.At(ScenarioMemory.PeopleTable, hero, 0x26), true);
        int lv = Memory.Read(ScenarioMemory.At(ScenarioMemory.PeopleTable, hero, 0x24), false);
        _seed = (uint)(((w26 + lv) * GameValues[ValueYear] * GameValues[ValueMonth] + GameValues[ValueDay] + GameValues[ValueTime]) << 8);
        bool nextSection = false, nextChapter = false;
        var run = Run_(baseAt, ip, gold, name, family, ref nextSection, ref nextChapter);
        if (nextSection) Section++;
        if (nextChapter)
        {
            // 함께 쓰는 시나리오는 장이 바뀌면 장 · 절을 0 으로 돌린다 — 맡은 일이 끝난 것이다(0x0043F889).
            Chapter = EndsAtChapterZero ? 0 : Chapter + 1;
            Section = 0;
            Array.Clear(_flags, 0, 32);
        }
        return run;
    }

    private Run Run_(int baseAt, int ip, int gold, string name, string family, ref bool nextSection, ref bool nextChapter)
    {
        var run = new Run { Gold = gold };
        Memory.Gold = gold;
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
                    int dest = (op >> 4) & 3, src = (op >> 2) & 3, w = op & 3;
                    if (w == 3)
                    {
                        if (dest == 0 && a2 is >= 1 and < 8) _vars[a1] = (ushort)GameValues[a2];
                        break;
                    }
                    if (w == 2) break;                              // 2 는 오류
                    if (dest == 3)
                    {
                        // 가리키개 a = 가리키개 b 자리의 dword 가 가리키는 곳(0x0043EB09) — 이름 포인터 따위를 따라간다.
                        _ptrs[a1] = ScenarioMemory.Decode(Memory.ReadDword(_ptrs[a2]));
                        break;
                    }
                    int v = src == 3 ? (len == 4 ? Word(ip + 2) : a2) : ValueOf(src, a2, w == 0);
                    Store(dest, a1, v, w == 0);
                    break;
                }
                case 1:
                {
                    int code = op & 0x23, src = (op >> 2) & 3;
                    bool toPtr = (op & 0x10) != 0;
                    int v = src switch
                    {
                        0 or 3 => ValueOf(src, a2),
                        2 => code is 0x20 or 0x21 ? ValueOf(2, a2, false) : ValueOf(2, a2) != 0 ? 1 : 0,
                        _ => ValueOf(1, a2, false),
                    };
                    if (code is 0x22 or 0x23)
                    {
                        var p = _ptrs[a1];
                        _ptrs[a1] = p with { Offset = p.Offset + (code == 0x22 ? v : -v) };
                        break;
                    }
                    int cur = toPtr ? Memory.Read(_ptrs[a1], false) : _vars[a1];
                    int r = code switch
                    {
                        0 => cur + v,
                        1 => cur - v,
                        2 => cur * v,
                        3 => v == 0 ? 0xFFFF : (cur & 0xFFFF) / v,
                        0x20 => cur | v,
                        _ => cur & v,
                    };
                    Store(toPtr ? 1 : 0, a1, r & 0xFFFF, !toPtr);
                    break;
                }
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
                    else if (ka == 2)
                    {
                        ok = (a1 < _flags.Length && _flags[a1]) == (a2 < _flags.Length && _flags[a2]);
                        target = Word(ip + 3);
                    }
                    else
                    {
                        int va = ValueOf(ka, a1), vb = ValueOf(kb, a2);
                        ok = c switch { 0 => va == vb, 1 => va != vb, 2 => va >= vb, _ => vb >= va };
                        target = Word(ip + 3);
                    }
                    if (!ok) { ip = baseAt + target; continue; }
                    break;
                }
                default:
                    switch (op)
                    {
                        case 0xC8 or 0xD4:
                            var (who, body) = ScenarioText.Render(Expand(_text[Word(ip + 1)]), name, family);
                            run.Lines.Add(new Line(who, body, window, face));
                            break;
                        case 0xC0:
                            if (a1 < 4 && a1 != window) { window = a1; face = -1; }
                            break;
                        case 0xCC: face = Word(ip + 1); break;
                        case 0xCD: face = _vars[a1]; break;
                        case 0xC9:
                        {
                            var options = ScenarioText.Render(Expand(_text[Word(ip + 2)]), name, family).Body
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
                        case 0xE4: Memory.Gold = _vars[a1]; break;
                        case 0xE6: Memory.Gold = (int)Math.Min(PlayerState.MaxGold, (long)Memory.Gold + _vars[a1]); break;
                        case 0xE7: Memory.Gold = Math.Max(0, Memory.Gold - _vars[a1]); break;
                        case 0xE9:
                        {
                            int yes = Choose != null ? Choose(run.Pending, ["예", "아니오"]) : 0;
                            if (Choose != null) run.Shown = run.Lines.Count;
                            if (a1 < _flags.Length) _flags[a1] = yes == 0;
                            break;
                        }
                        case 0xEA: _vars[a1] = (ushort)(Memory.Gold / 10000); break;
                        case 0xEB:
                        {
                            int n = Word(ip + 2);
                            if (n == 0) { _vars[a1] = 0; break; }
                            _seed = unchecked(_seed * 0x5D588B65u + 1);
                            _vars[a1] = (ushort)((int)((_seed >> 8) & 0x7FFF) % n);
                            break;
                        }
                        case 0xEC: _seed = (uint)((short)_vars[a1] << 8); break;
                        case 0xED: _vars[a1] = (ushort)(_seed >> 8); break;
                        case 0xEE: _vars[a1] = (ushort)FreeRoom; break;
                        case 0xE2: Cargo?.Invoke(_vars[a1] & 0xFF, _vars[a2]); break;
                        case 0xE3: _vars[a2] = (ushort)(Cargo?.Invoke(_vars[a1] & 0xFF, int.MinValue) ?? 0); break;
                        case 0xFB:
                        {
                            int heroP = Memory.Read(new ScenarioMemory.Pointer(ScenarioMemory.GoldTable, ScenarioMemory.HeroField), false);
                            for (int i = 0; i < 30; i++)
                            {
                                var slot = new ScenarioMemory.Pointer(6, 3 + i);
                                if (Memory.Read(slot, false) != 0xFF) continue;
                                Memory.Write(slot, a1, false);
                                break;
                            }
                            Memory.Write(ScenarioMemory.At(ScenarioMemory.PeopleTable, a1, 0x2E), 6, false);
                            foreach (int f in (int[])[0x2C, 0x2D])
                                Memory.Write(ScenarioMemory.At(ScenarioMemory.PeopleTable, a1, f),
                                             Memory.Read(ScenarioMemory.At(ScenarioMemory.PeopleTable, heroP, f), false), false);
                            run.Joined.Add(a1);
                            break;
                        }
                        case 0xD0 or 0xDC:
                        {
                            int rec = _code[ip + 3];
                            if (op == 0xD0) rec = _vars[rec] & 0xFF;
                            _ptrs[a1] = ScenarioMemory.At(a2, rec, _code[ip + 4]);
                            break;
                        }
                        case 0xFE:
                            ip = baseAt + Word(ip + 1);
                            continue;
                        case 0xF0: nextSection = true; break;
                        case 0xF1: nextChapter = true; break;
                        case 0xF8: run.Stopped = true; break;
                        case 0xF2 or 0xF4:
                            run.Gold = Memory.Gold;
                            return run;
                    }
                    break;
            }
            ip += len;
        }
        run.Gold = Memory.Gold;
        return run;
    }
}
