using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 게임 말 <c>MESSAGE.DAT</c> — 머리가 2바이트 빅엔디언 자리들이고 첫 자리 / 2 가 말 수(1179)다. 글은 cp949, 끝에 0.
/// </summary>
/// <remarks>
/// 원본 손이 <c>0x00436670(번호)</c> · <c>0x0042D0B0(번호, …)</c> 로 띄우는 말들이다. 안에 <c>%s</c> · <c>%d</c> 가 있어
/// printf 처럼 채운다(<see cref="Format"/>). <c>\n</c> 은 줄 바꿈이다.
/// </remarks>
public sealed class GameMessages
{
    private readonly string[] _lines;

    private GameMessages(string[] lines) => _lines = lines;

    public int Count => _lines.Length;

    public string this[int id] => id >= 0 && id < _lines.Length ? _lines[id] : "";

    public static GameMessages Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static GameMessages FromBytes(byte[] d)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        int Off(int i) => d[i * 2] << 8 | d[i * 2 + 1];
        int n = d.Length >= 2 ? Off(0) / 2 : 0;
        var lines = new string[n];
        for (int i = 0; i < n; i++)
        {
            int a = Off(i), b = i + 1 < n ? Off(i + 1) : d.Length;
            lines[i] = a < d.Length && b <= d.Length && b >= a ? cp949.GetString(d, a, b - a).TrimEnd('\0') : "";
        }
        return new GameMessages(lines);
    }

    /// <summary>printf 꼴로 채운다 — <c>%s</c> · <c>%d</c> · <c>%%</c>. 모자란 인자는 빈 글로.</summary>
    public string Format(int id, params object[] args)
    {
        string f = this[id];
        var sb = new StringBuilder();
        int k = 0;
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] == '%' && i + 1 < f.Length)
            {
                // %5d · %ld · %1d 처럼 폭 · 길이가 붙은 것도 받는다.
                int j = i + 1;
                while (j < f.Length && (char.IsDigit(f[j]) || f[j] is '-' or 'l')) j++;
                if (j < f.Length && f[j] is 's' or 'd' or 'u' or 'c')
                {
                    sb.Append(k < args.Length ? args[k++]?.ToString() : "");
                    i = j;
                    continue;
                }
                if (f[i + 1] == '%') { sb.Append('%'); i++; continue; }
            }
            sb.Append(f[i]);
        }
        return Particles(sb.ToString());
    }

    /// <summary>
    /// 「(가)」 「(는)」 「(을)」 「(를)」 「(은)」 「(이)」 「(와)」 「(과)」 를 앞 글자 받침에 맞춘다. 숫자는 우리말로 읽은 끝소리로 본다
    /// (0 영 · 1 일 · 3 삼 · 6 육 · 7 칠 · 8 팔 · 10 십 은 받침이 있다).
    /// 「(이라면)」 「(이라고)」 「(라고)」 「(이렸다)」 처럼 「이」 가 붙었다 떨어지는 것도 받침이 없으면 「이」 를 뗀다.
    /// </summary>
    public static string Particles(string s)
    {
        if (!s.Contains('(')) return s;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(' && sb.Length > 0 && Copula(s, i) is { } word)
            {
                string bare = word.StartsWith('이') ? word[1..] : word;
                sb.Append(Batchim(sb[^1]) ? "이" + bare : bare);
                i += word.Length + 1;
                continue;
            }
            if (s[i] == '(' && i + 2 < s.Length && s[i + 2] == ')' && sb.Length > 0
                && s[i + 1] is '가' or '이' or '는' or '은' or '을' or '를' or '와' or '과')
            {
                bool batchim = Batchim(sb[^1]);
                char p = s[i + 1];
                sb.Append(p switch
                {
                    '가' or '이' => batchim ? '이' : '가',
                    '는' or '은' => batchim ? '은' : '는',
                    '을' or '를' => batchim ? '을' : '를',
                    _ => batchim ? '과' : '와',
                });
                i += 2;
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static bool Batchim(char before) => before switch
    {
        >= '가' and <= '힣' => (before - '가') % 28 != 0,
        >= '0' and <= '9' => "0136789".Contains(before),
        _ => false,
    };

    /// <summary>「(이라면)」 같은 서술격 토씨 — <paramref name="at"/> 의 괄호 안 낱말, 아니면 null.</summary>
    private static string? Copula(string s, int at)
    {
        foreach (string w in (string[])["이라면", "이라고", "라고", "이렸다"])
            if (at + w.Length + 1 < s.Length + 1 && string.CompareOrdinal(s, at + 1, w, 0, w.Length) == 0
                && at + w.Length + 1 < s.Length && s[at + w.Length + 1] == ')')
                return w;
        return null;
    }
}
