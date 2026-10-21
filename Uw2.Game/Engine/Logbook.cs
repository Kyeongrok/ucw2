namespace Uw2.Game.Engine;

/// <summary>
/// 항해일지 — <c>0x0046E498</c>, 16 바이트 x 48(세이브 칸 몸통 뒤 0x300 바이트). 한 줄은 낱말 여섯: 날(= (해 x 12 + 달) x 31 + 날) · 갈래 · 넷.
/// 적는 손 <c>0x00444560</c> 은 빈 줄(날 0xFFFF)에 쓰고, 다 찼으면 한 줄씩 올려 끝에 쓴다(<c>0x00444610</c>).
/// </summary>
/// <remarks>
/// <code>
///   0  기항   (날 수, 항구)              「우리 함대는 %d일간의 항해를 끝내고 %s에 기항했다.」(0x16E, 0x0041AA1D)
///   1  출항   (물자 날 수, 항구)          「우리 함대는 %d일분의 물자를 갖추어 %s항을 떠났다.」(0x16F, 0x00422C1F)
///   2  해전   (남 함대 번호, 끝 0 ~ 2)     「우리 함대는, %s %s대와 전투%s」(0x345 — 나라, 목록 0x34 함 · 상선 · 전함, 목록 0x35 끝)
///   9  발견   (항구)                     「%s항을 발견했다.」(0x17D)
///   12 구입   (교역품, 값 위 · 아래, 개수)  「%s(를) 금화 %ld닢으로 %d개 구입했다.」(0x180, 0x00420AA3)
/// </code>
/// </remarks>
public sealed class Logbook
{
    public const int Lines = 48, LineSize = 16, Bytes = Lines * LineSize, Empty = 0xFFFF;
    public const int Arrived = 0, Departed = 1, Battle = 2, Found = 9, Bought = 12;

    public readonly record struct Entry(int Year, int Month, int Day, int Type, int A, int B, int C, int D);

    private readonly List<Entry> _lines = [];

    public IReadOnlyList<Entry> Entries => _lines;

    public void Add(int year, int month, int day, int type, int a = 0, int b = 0, int c = 0, int d = 0)
    {
        if (_lines.Count >= Lines) _lines.RemoveAt(0);
        _lines.Add(new Entry(year, month, day, type, a, b, c, d));
    }

    public byte[] Export()
    {
        var b = new byte[Bytes];
        Array.Fill(b, (byte)0xFF);
        for (int i = 0; i < _lines.Count; i++)
        {
            var e = _lines[i];
            int[] w = [(e.Year * 12 + e.Month) * 31 + e.Day, e.Type, e.A, e.B, e.C, e.D];
            for (int k = 0; k < 6; k++) BitConverter.GetBytes((ushort)w[k]).CopyTo(b, i * LineSize + k * 2);
        }
        return b;
    }

    public void Import(byte[]? b)
    {
        _lines.Clear();
        if (b is not { Length: >= Bytes }) return;
        for (int i = 0; i < Lines; i++)
        {
            int W(int k) => BitConverter.ToUInt16(b, i * LineSize + k * 2);
            int date = W(0);
            if (date == Empty) break;
            int day = date % 31, ym = date / 31;
            _lines.Add(new Entry(ym / 12, ym % 12, day, W(1), W(2), W(3), W(4), W(5)));
        }
    }
}
