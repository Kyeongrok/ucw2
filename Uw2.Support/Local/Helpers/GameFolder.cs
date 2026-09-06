namespace Uw2.Support.Local.Helpers;

/// <summary>
/// 게임 폴더를 찾는다. <b>Win95 이식판을 기준</b>으로 잡되 DOS 원판도 받는다.
/// </summary>
/// <remarks>
/// 규칙을 캐는 데 쓴 것이 Win95 이식판 <c>KOUKAI2.EXE</c>(32비트 PE)라 그쪽을 기준 삼는다.
/// 자료는 두 판이 거의 같다 — <c>CHIP_NO.DAT</c>·<c>WINDCUR.DAT</c>·<c>MONSTER.DAT</c>·
/// 그림표가 바이트까지 같고, 세계지도는 583,200칸 가운데 31칸만 다르다.
/// 갈리는 것은 <b>칩을 담는 꼴</b>뿐이고 그것은 <see cref="Formats.ChipSheet.Open"/> 이 가린다.
/// </remarks>
public static class GameFolder
{
    /// <summary>이 파일들이 다 있어야 게임 폴더로 본다.</summary>
    public static readonly string[] Needed =
        ["WORLDMAP.LZW", "DATA1.LZW", "PORTMAP.LZW", "PORTCHIP.LZW", "CHIP_NO.DAT", "WINDCUR.DAT"];

    /// <summary>Win95 이식판임을 알아보는 파일.</summary>
    public const string Win95Exe = "KOUKAI2.EXE";

    /// <summary>DOS 원판임을 알아보는 파일.</summary>
    public const string DosExe = "MAIN.EXE";

    /// <summary>어느 판인지.</summary>
    public enum Build
    {
        /// <summary>가려내지 못했다. 자료만 있는 폴더다.</summary>
        Unknown,
        /// <summary>Win95 이식판 — 기준 판.</summary>
        Win95,
        /// <summary>DOS 원판.</summary>
        Dos,
    }

    /// <summary>흔히 놓이는 자리들. <b>Win95 이식판을 먼저</b> 본다.</summary>
    public static IEnumerable<string> Guesses()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Path.Combine(desktop, "DH2W95Kor_Win_Test2");
        yield return Path.Combine(desktop, "DH2W95Kor");
        yield return Path.Combine(desktop, "대항해시대2");

        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "game");
    }

    /// <summary>그 폴더가 게임 폴더인지.</summary>
    public static bool IsGameFolder(string dir) =>
        Directory.Exists(dir) && Needed.All(n => File.Exists(Path.Combine(dir, n)));

    /// <summary>어느 판인지 가린다.</summary>
    public static Build BuildOf(string dir)
    {
        if (File.Exists(Path.Combine(dir, Win95Exe))) return Build.Win95;
        if (File.Exists(Path.Combine(dir, DosExe))) return Build.Dos;
        return Build.Unknown;
    }

    /// <summary>판 이름을 사람 말로.</summary>
    public static string BuildName(Build b) => b switch
    {
        Build.Win95 => "Win95 이식판",
        Build.Dos => "DOS 원판",
        _ => "판 모름",
    };

    /// <summary>찾아 본다. 없으면 null.</summary>
    public static string? Find()
    {
        foreach (var g in Guesses())
            if (IsGameFolder(g)) return g;
        return null;
    }

    /// <summary>왜 못 찾았는지 사람 말로.</summary>
    public static string Explain(string? tried = null)
    {
        string head = tried == null
            ? "게임 폴더를 못 찾았습니다."
            : $"‘{tried}’ 은 게임 폴더가 아닙니다.";
        return $"{head} 다음 파일이 다 있는 폴더를 골라 주세요 — {string.Join(" · ", Needed)}";
    }
}
