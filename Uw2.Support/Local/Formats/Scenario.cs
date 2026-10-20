namespace Uw2.Support.Local.Formats;

/// <summary>
/// 시나리오 바이트코드 <c>SNRn.DAT</c> — 주인공마다 하나(머리 <c>"SNDT" 00 사람번호</c>), <c>SNR0.DAT</c> 은 함께 쓰는 것.
/// </summary>
/// <remarks>
/// 이벤트 기계(<c>KOUKAI2.EXE 0x0043EFB0</c>)가 명령 바이트 <c>0xC0~0xFF</c> 를 바이트표 <c>0x0043F704</c> 로 손 번호에 대고
/// 손표 <c>0x0043F668</c>(서른아홉)로 뛴다. 0xC0 아래 바이트는 글(<c>SNRn.MES</c>) 쪽이다.
/// <code>
///   머리       "SNDT" 00 id, 0x12 부터 이벤트 첫 자리들(빅엔디언) … 0xFFFFFFFF
///   F9 t _     배 주기 — 0x0043F55D → 0x0043DEA0(t). 둘째 바이트는 읽고 버린다
/// </code>
/// 주인공 첫 배는 <b>첫 이벤트 안의 첫 F9</b> 다. 조안 · 옷토 · 에르네스트 · 피에트로 · 알은 카라벨 라티나(5),
/// 카탈리나는 갤리온(10)이다. 세이브(KOUKAI2.DAT)의 조안 배 「헤르메스Ⅱ세」가 카라벨 라티나인 것과도 맞는다.
/// 옷토는 첫 이벤트 뒤쪽에 갤리온(10)을 받는 F9 가 하나 더 있다 — 갈래를 타는 듯해 아직 첫 것만 쓴다.
/// </remarks>
public static class Scenario
{
    public const byte GiveShip = 0xF9;

    /// <summary>
    /// 명령 하나의 길이 — 일을 마친 손이 이벤트 자리(<c>0x004702B8</c>)를 이만큼 민다.
    /// <list type="bullet">
    /// <item><c>00xxxxxx</c> 넣기 · 셈(<c>0x0043E830</c>) — 3, 「넣기」(셈 0)에 줄 것이 바로 값(갈래 3)이면 4(<c>0C v hh ll</c>)</item>
    /// <item><c>01xxxxxx</c>(<c>0x0043EB90</c>) — 3</item>
    /// <item><c>10xxxxxx</c> 견주어 뛰기(<c>0x0043ED90</c>) — 5(맞으면 다음으로, 아니면 끝 두 바이트 자리로), 받을 곳 갈래 2 면 4</item>
    /// <item><c>11xxxxxx</c> 명령 — 표(<see cref="CommandLength"/>)</item>
    /// </list>
    /// 모르는 명령은 0 을 돌려준다.
    /// </summary>
    public static int LengthAt(ReadOnlySpan<byte> d, int i)
    {
        byte op = d[i];
        switch (op >> 6)
        {
            case 0:
                int dest = (op >> 4) & 3, src = (op >> 2) & 3, calc = op & 3;
                return dest != 2 && calc == 0 && src == 3 ? 4 : 3;
            case 1: return 3;
            case 2: return ((op >> 4) & 3) == 2 ? 4 : 5;
            default: return CommandLength(op);
        }
    }

    /// <summary>
    /// 명령 바이트(0xC0~0xFF)의 길이 — 손마다 <c>0x004702B8</c> 에 더하는 값. 0 은 손이 없는(오류) 명령이다.
    /// </summary>
    public static int CommandLength(byte op) => op switch
    {
        0xC3 or 0xC4 or 0xC7 or 0xF0 or 0xF1 or 0xF2 or 0xF8 or 0xFF => 1,
        0xC0 or 0xC5 or 0xCA or 0xCD or 0xD1 or 0xE4 or 0xE6 or 0xE7 or 0xE8 or 0xE9 or 0xEA
            or 0xEC or 0xED or 0xEE or 0xF4 or 0xFB => 2,
        0xC8 or 0xCC or 0xD4 or 0xE2 or 0xE3 or 0xF9 or 0xFC or 0xFE => 3,
        0xC9 or 0xEB or 0xFA => 4,
        0xD0 or 0xDC => 5,
        0xCB => 6,
        _ => 0,
    };

    /// <summary>사람이 읽을 이름 — 뜻을 굳힌 것만.</summary>
    public static string NameOf(byte op) => op switch
    {
        < 0x40 => "넣기",
        < 0x80 => "01",
        < 0xC0 => "견주어뛰기",
        0xE4 => "돈=",
        0xE6 => "돈+=",
        0xE7 => "돈-=",
        0xF2 => "그침",
        0xF4 => "끝맺음",
        0xF9 => "배주기",
        0xFE => "뛰기",
        0xFF => "끝",
        _ => $"명령{op:X2}",
    };

    /// <summary>한 이벤트를 늘어놓는다 — (자리, 바이트들). 모르는 바이트는 한 바이트씩 넘긴다.</summary>
    public static IEnumerable<(int At, byte[] Bytes)> Disassemble(byte[] d, int start, int end)
    {
        for (int i = start; i < end;)
        {
            int n = LengthAt(d, i);
            if (n <= 0 || i + n > end) n = 1;
            yield return (i, d[i..(i + n)]);
            i += n;
        }
    }

    /// <summary>이벤트 첫 자리들 — 머리 0x12 부터 4바이트씩, FFFF FFFF 까지.</summary>
    public static List<int> Events(byte[] d)
    {
        var list = new List<int>();
        for (int o = 0x12; o + 4 <= d.Length; o += 4)
        {
            if (d[o] == 0xFF && d[o + 1] == 0xFF) break;
            list.Add(d[o] << 8 | d[o + 1]);
        }
        return list;
    }

    /// <summary>그 파일이 누구 것인지(머리 +5). 함께 쓰는 SNR0 은 0x80 이다.</summary>
    public static int OwnerOf(byte[] d) => d.Length > 6 && d[0] == (byte)'S' && d[1] == (byte)'N' ? d[5] : -1;

    /// <summary>첫 이벤트의 처음과 끝(둘째 이벤트 자리). 머리 0x12 부터 4바이트씩, 앞 두 바이트가 빅엔디언 자리다.</summary>
    public static (int Start, int End) FirstEvent(byte[] d)
    {
        int start = d[0x12] << 8 | d[0x13], end = d[0x16] << 8 | d[0x17];
        return (start, end > start && end <= d.Length ? end : d.Length);
    }

    /// <summary>첫 이벤트에서 처음 주는 선종. 없으면 -1.</summary>
    public static int FirstShip(byte[] d)
    {
        var (start, end) = FirstEvent(d);
        for (int i = start; i + 2 < end; i++)
            if (d[i] == GiveShip && d[i + 1] < StartData.ShipTypeCount) return d[i + 1];
        return -1;
    }

    /// <summary>
    /// 첫 이벤트에서 처음 만나는 돈 명령의 값 — <c>0C v hh ll</c>(변수[v] = hhll) 바로 뒤 <c>E4 v</c>(돈 =) · <c>E6 v</c>(돈 +=).
    /// <b>어림이다</b> — 조안 · 옷토 · 알은 더하기가 여러 번(대사 갈래)이라 첫 것만 쓴다. 없으면 0.
    /// </summary>
    public static int FirstGold(byte[] d)
    {
        var (start, end) = FirstEvent(d);
        for (int i = start; i + 5 < end; i++)
            if (d[i] == 0x0C && (d[i + 4] == 0xE4 || d[i + 4] == 0xE6) && d[i + 5] == d[i + 1])
                return d[i + 2] << 8 | d[i + 3];
        return 0;
    }

    /// <summary>게임 폴더의 SNR1~6 을 읽어 사람 번호마다 첫 돈을 낸다.</summary>
    public static int[] HeroGold(string gameDirectory)
    {
        var gold = new int[6];
        for (int n = 1; n <= 6; n++)
        {
            string path = Path.Combine(gameDirectory, $"SNR{n}.DAT");
            if (!File.Exists(path)) continue;
            var d = File.ReadAllBytes(path);
            int who = OwnerOf(d);
            if (who is >= 0 and < 6) gold[who] = FirstGold(d);
        }
        return gold;
    }

    /// <summary>게임 폴더의 SNR1~6 을 읽어 사람 번호마다 첫 배를 낸다.</summary>
    public static int[] HeroShips(string gameDirectory)
    {
        var ships = Enumerable.Repeat(-1, 6).ToArray();
        for (int n = 1; n <= 6; n++)
        {
            string path = Path.Combine(gameDirectory, $"SNR{n}.DAT");
            if (!File.Exists(path)) continue;
            var d = File.ReadAllBytes(path);
            int who = OwnerOf(d);
            if (who is >= 0 and < 6) ships[who] = FirstShip(d);
        }
        return ships;
    }
}
