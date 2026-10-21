using System.IO;
using System.Windows.Media;

namespace Uw2.Game.UI.Audio;

/// <summary>
/// 배경 음악 — 원본은 CD 음악(<c>koeicda.dll</c> <c>CDAudioPlayTrack</c>, <c>0x00440429</c>)을 곡 번호로 돈다(<c>0x004405B0</c>).
/// 곡 번호 n 은 CD 트랙 n + 2 다(트랙 1 은 자료). 게임 폴더의 <c>bgm/TrackNN.mp3</c>(_inmm 이 CD 대신 트는 것)를 끝없이 돈다.
/// </summary>
/// <remarks>
/// 자리마다 곡은 설정 변수(<c>0x0049F878</c> ~ <c>0x0049F8F4</c>)에 들고 처음 값은 <c>0x0045E3E8</c> 표다(<c>0x004406F0</c> · <c>0x00440630</c>).
/// </remarks>
public static class Music
{
    /// <summary>여는 화면(<c>0x0049F8A0</c>) · 제목(<c>0x0049F8A8</c>) · 바다(<c>0x0049F87C</c>) · 작은 항구(<c>0x0049F8AC</c>) · 엔딩(<c>0x0049F890</c>).</summary>
    public const int Opening = 0, Title = 1, Sea = 2, SmallPort = 7, Ending = 11;

    /// <summary>해전 판(<c>0x0040FAD7</c>, <c>0x0049F8F4</c>) · 왕궁(<c>0x0049F884</c>) · 주점(<c>0x0049F888</c>) — 건물에 들 때 <c>0x00431F82</c>.</summary>
    public const int Battle = 8, Palace = 9, Pub = 10;

    /// <summary>
    /// 도시 곡(<c>0x004319B0</c>) — 항구 형편 +0x21(문화권 0 ~ 12)을 갈래 다섯으로 접어 곡을 고른다. 100 넘는 항구는 <see cref="SmallPort"/>.
    /// </summary>
    public static int Town(int culture) =>
        culture is >= 0 and < 13 ? TownSongs[TownGroup[culture]] : TownSongs[0];

    /// <summary>
    /// 시나리오 <c>CA k</c> 의 곡 표(<c>0x0043D2F0</c> 가 설정 변수에서 채우는 <c>0x0049F778</c>) — 20 · 21 은 곡이 아닌 몫이라(<c>0x14</c> · <c>0x15</c>) 넘긴다.
    /// </summary>
    public static void PlayScenario(int k)
    {
        if (k >= 0 && k < ScenarioSongs.Length) Play(ScenarioSongs[k]);
    }

    private static readonly int[] ScenarioSongs = [Opening, Ending, Ending, Title, Sea, Sea, Sea, Sea, Sea, Sea, 5, 3, 4, 3, 6, SmallPort, Battle, Battle, Palace, Pub];

    private static readonly int[] TownGroup = [0, 0, 0, 1, 2, 1, 1, 1, 1, 2, 3, 4, 4];
    private static readonly int[] TownSongs = [5, 3, 4, 3, 6];

    private static MediaPlayer? _player;
    private static int _now = -1;

    /// <summary>원본 게임 폴더 — <c>bgm</c> 가 그 아래에 있다. 없으면 소리를 내지 않는다.</summary>
    public static string? Folder { get; set; }

    /// <summary>음악을 켜고 끄기(설정 「BGM」).</summary>
    public static bool Enabled { get; set; } = true;

    public static void Play(int song)
    {
        if (!Enabled || song == _now || Folder is null) return;
        string path = Path.Combine(Folder, "bgm", $"Track{song + 2:00}.mp3");
        if (!File.Exists(path)) return;
        _now = song;
        if (_player == null)
        {
            _player = new MediaPlayer { Volume = 0.5 };
            _player.MediaEnded += (_, _) => { _player.Position = TimeSpan.Zero; _player.Play(); };
        }
        _player.Open(new Uri(path));
        _player.Play();
    }

    public static void Stop()
    {
        _player?.Stop();
        _now = -1;
    }

    /// <summary>켜고 끈다 — 끄면 멈추고, 켜면 <paramref name="resume"/> 곡을 다시 튼다.</summary>
    public static void Toggle(int resume)
    {
        int was = _now;
        Enabled = !Enabled;
        if (!Enabled) Stop();
        else Play(was >= 0 ? was : resume);
    }
}
