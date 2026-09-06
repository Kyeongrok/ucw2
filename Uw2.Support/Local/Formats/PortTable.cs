using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 항구 표 — 이름과 세계지도 위 자리. <b>리스본이 0번</b>이다.
/// </summary>
/// <remarks>
/// 레코드가 130개 이어진다. 짜임은 판마다 같은데 <b>이름 칸 길이가 다르다</b>.
/// <code>
///   [0]      나라·문화권 한 바이트   (뜻은 아직 안 갈렸다)
///   [1..2]   x  (u16 LE, 칩 눈금 0~2159)
///   [3..4]   y  (u16 LE, 칩 눈금 0~1079)
///   [5..]    이름, NUL 로 채움      DOS 15바이트(레코드 20) · Win95 세이브 17바이트(레코드 22)
/// </code>
///
/// <para>어디 있나</para>
/// 판마다 자리도 글자도 달라서 <b>찾아서 읽는다</b>(<see cref="Find"/>).
/// <code>
///   DOS  MAIN.EXE            0x040903   한글(cp949)
///   DOS  KOUKAI2.DAT         0x0053BA   한글 — MAIN.EXE 것과 바이트까지 같다
///   Win95 KOUKAI2.EXE        0x05C7DF   일본어(cp932) — 이식판은 원본 이름을 그대로 뒀다
///   Win95 KOUKAI2.DAT        0x0075E9   한글
///   Win95 KOUKAI2-han8.exe   0x053624   한글
/// </code>
///
/// <para>좌표가 맞는지</para>
/// 서에서 동으로 리스본 120 · 세빌리아 142 · 발렌시아 178 · 바르셀로나 194 ·
/// 마르세이유 212 · 제노바 230 · 베네치아 258 · 이스탄불 352 로, 실제 차례와 같다.
/// 눈금은 <b>1도에 여섯 칩</b>이다(2160/360). 리스본(-9.14도)과 이스탄불(28.98도)로 재면
/// 232칩 / 38.12도 = 6.09 로 딱 맞는다.
/// </remarks>
public sealed class PortTable
{
    /// <summary>레코드 크기.</summary>
    public const int RecordSize = 20;

    /// <summary>이름 칸 크기.</summary>
    public const int NameSize = 15;

    /// <summary>표에 든 항구 수.</summary>
    public const int Expected = 130;

    /// <summary>리스본. 표의 첫 칸이고 게임 시작 항구다.</summary>
    public const int Lisbon = 0;

    /// <summary>항구 하나.</summary>
    /// <param name="Index">표에서의 차례. <see cref="PortMap"/> 의 차례와 같다.</param>
    /// <param name="Name">이름.</param>
    /// <param name="X">세계지도 위 자리(칩 눈금).</param>
    /// <param name="Y">〃</param>
    /// <param name="Nation">나라·문화권 한 바이트. 뜻은 아직 안 갈렸다.</param>
    public readonly record struct Port(int Index, string Name, int X, int Y, byte Nation)
    {
        /// <summary>세계지도 칸 자리.</summary>
        public (int X, int Y) Cell => (X / ChipMap.PerCell, Y / ChipMap.PerCell);
    }

    /// <summary>표에 든 항구들.</summary>
    public IReadOnlyList<Port> Ports { get; }

    /// <summary>어느 파일에서 읽었는지.</summary>
    public string Source { get; }

    private PortTable(List<Port> ports, string source) { Ports = ports; Source = source; }

    /// <summary>구워 둔 목록에서 바로 만든다.</summary>
    public static PortTable FromPorts(IEnumerable<Port> ports) => new([.. ports], "asset");

    /// <summary>이름으로 찾는다. 없으면 null.</summary>
    public Port? ByName(string name) =>
        Ports.FirstOrDefault(p => p.Name == name) is { Name.Length: > 0 } p ? p : null;

    /// <summary>
    /// 게임 폴더에서 읽는다. 판마다 자리가 달라 <b>파일을 훑어 찾는다</b>.
    /// </summary>
    /// <remarks>
    /// 한글판을 먼저 본다 — 이식판 EXE 에는 일본어 이름이 들어 있어 그쪽이 뒤다.
    /// </remarks>
    public static PortTable? Load(string gameDirectory)
    {
        string[] order = ["KOUKAI2.DAT", "MAIN.EXE", "KOUKAI2-han8.exe", "KOUKAI2.EXE"];
        foreach (var name in order)
        {
            string path = Path.Combine(gameDirectory, name);
            if (!File.Exists(path)) continue;
            var t = Find(File.ReadAllBytes(path), name);
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>머리(나라 + x + y) 크기. 이름 칸은 <c>레코드 - 이것</c> 이다.</summary>
    public const int HeadSize = 5;

    /// <summary>대 보는 레코드 크기. 판마다 이름 칸 길이가 달라 넉넉히 훑는다.</summary>
    private const int MinRecord = 16, MaxRecord = 40;

    /// <summary>표로 보려면 레코드가 적어도 이만큼은 이어져야 한다.</summary>
    private const int MinRun = 64;

    /// <summary>표를 찾은 레코드 크기.</summary>
    public int Stride { get; private set; } = RecordSize;

    /// <summary>
    /// 파일을 훑어 표를 찾는다. 못 찾으면 null.
    /// </summary>
    /// <remarks>
    /// 자리도 레코드 크기도 판마다 달라서 <b>둘 다 찾는다</b> — DOS <c>MAIN.EXE</c> 는 20바이트
    /// (이름 칸 15), Win95 세이브는 <b>22바이트</b>(이름 칸 17)다.
    /// </remarks>
    public static PortTable? Find(ReadOnlySpan<byte> file, string source)
    {
        int best = -1, bestRun = 0, bestStride = RecordSize;

        for (int stride = MinRecord; stride <= MaxRecord; stride += 2)
        {
            for (int at = 0; at + stride * MinRun <= file.Length; at++)
            {
                // 값싼 걸름 — 여기서 거의 다 걸러진다.
                int x = file[at + 1] | (file[at + 2] << 8);
                int y = file[at + 3] | (file[at + 4] << 8);
                if (x <= 0 || x >= ChipMap.Width || y <= 0 || y >= ChipMap.Height) continue;
                if (file[at + HeadSize] == 0) continue;

                int run = RunAt(file, at, stride);
                if (run > bestRun) { bestRun = run; best = at; bestStride = stride; }
                if (bestRun >= Expected) break;
            }
            if (bestRun >= Expected) break;
        }
        if (best < 0 || bestRun < MinRun) return null;

        var ports = new List<Port>(bestRun);
        for (int i = 0; i < bestRun; i++)
        {
            var r = file.Slice(best + i * bestStride, bestStride);
            ports.Add(new Port(i, DecodeName(r[HeadSize..]),
                               r[1] | (r[2] << 8), r[3] | (r[4] << 8), r[0]));
        }
        return new PortTable(ports, source) { Stride = bestStride };
    }

    /// <summary>그 자리에서 레코드가 몇 개나 이어지는지.</summary>
    private static int RunAt(ReadOnlySpan<byte> file, int at, int stride)
    {
        int n = 0;
        while (at + (n + 1) * stride <= file.Length && n < Expected)
        {
            var r = file.Slice(at + n * stride, stride);
            int x = r[1] | (r[2] << 8), y = r[3] | (r[4] << 8);
            if (x <= 0 || x >= ChipMap.Width || y <= 0 || y >= ChipMap.Height) break;

            var name = r[HeadSize..];
            int z = name.IndexOf((byte)0);
            if (z <= 0) break;                    // 빈 이름도, NUL 없는 것도 표가 아니다
            bool ok = true;
            for (int k = 0; k < z; k++) if (name[k] < 0x20) { ok = false; break; }
            if (!ok) break;
            n++;
        }
        return n;
    }

    /// <summary>이름을 푼다. 한글판이면 cp949, 이식판이면 cp932 다.</summary>
    private static string DecodeName(ReadOnlySpan<byte> field)
    {
        int z = field.IndexOf((byte)0);
        if (z <= 0) return "";
        var raw = field[..z].ToArray();

        foreach (int cp in (int[])[949, 932])
        {
            try
            {
                string s = Encoding.GetEncoding(cp).GetString(raw);
                if (!s.Contains('�')) return s;
            }
            catch (ArgumentException) { /* 코드페이지가 없으면 다음 것으로 */ }
            catch (NotSupportedException) { }
        }
        return Encoding.Latin1.GetString(raw);
    }

    /// <summary>
    /// cp949 · cp932 를 쓰려면 한 번 등록해야 한다. 프로그램 시작할 때 부른다.
    /// </summary>
    public static void RegisterEncodings() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
