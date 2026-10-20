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
