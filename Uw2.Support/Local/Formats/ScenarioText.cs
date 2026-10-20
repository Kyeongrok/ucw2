using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 시나리오 글 <c>SNRn.MES</c> — 머리가 글 자리(빅엔디언 4바이트)들이고 첫 자리 / 4 가 글 수다. 글은 cp949.
/// </summary>
/// <remarks>
/// 글 안의 표시:
/// <code>
///   §이름§       말하는 사람(글 맨 앞)
///   /            줄 바꿈
///   $n · $s      주인공 이름 · 성
///   (이냐) (야) (이)  앞 글자의 받침에 따라 고르는 토씨
/// </code>
/// </remarks>
public sealed class ScenarioText
{
    private readonly string[] _lines;

    private ScenarioText(string[] lines) => _lines = lines;

    public int Count => _lines.Length;

    public string this[int i] => i >= 0 && i < _lines.Length ? _lines[i] : "";

    public static ScenarioText Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static ScenarioText FromBytes(byte[] d)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        int Off(int i) => d[i * 4] << 24 | d[i * 4 + 1] << 16 | d[i * 4 + 2] << 8 | d[i * 4 + 3];
        int n = Off(0) / 4;
        var lines = new string[Math.Max(0, n - 1)];
        for (int i = 0; i < lines.Length; i++)
        {
            int a = Off(i), b = Off(i + 1);
            lines[i] = a >= 0 && b <= d.Length && b >= a ? cp949.GetString(d, a, b - a).TrimEnd('\0', ' ') : "";
        }
        return new ScenarioText(lines);
    }

    /// <summary>말하는 사람과 본문으로 가르고 표시를 푼다.</summary>
    public static (string Speaker, string Body) Render(string raw, string name, string family)
    {
        string speaker = "", body = raw;
        if (body.StartsWith('§'))
        {
            int end = body.IndexOf('§', 1);
            if (end > 0)
            {
                speaker = body[1..end];
                body = body[(end + 1)..].TrimStart('/');
            }
        }
        return (Fill(speaker, name, family), Fill(body, name, family).Replace('/', '\n'));
    }

    private static string Fill(string s, string name, string family)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '$' && i + 1 < s.Length && (s[i + 1] == 'n' || s[i + 1] == 's'))
            {
                sb.Append(s[i + 1] == 'n' ? name : family);
                i++;
                continue;
            }
            if (s[i] == '(')
            {
                int close = s.IndexOf(')', i);
                if (close > i && close - i <= 4 && sb.Length > 0)
                {
                    sb.Append(Particle(s[(i + 1)..close], sb[^1]));
                    i = close;
                    continue;
                }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>받침이 있으면 「이」 붙은 꼴, 없으면 뗀 꼴 — (이냐) → 이냐/냐, (야) → 아/야, (이) → 이/없음.</summary>
    private static string Particle(string p, char before)
    {
        bool batchim = before is >= '가' and <= '힣' && (before - '가') % 28 != 0;
        return p switch
        {
            "야" => batchim ? "아" : "야",
            "이" => batchim ? "이" : "",
            _ when p.StartsWith('이') => batchim ? p : p[1..],
            _ => p,
        };
    }
}
