using System.IO;
using System.Windows.Media;

namespace Uw2.Game.UI.Audio;

/// <summary>
/// 효과음 — 원본은 <c>koeidsnd.dll</c>(<c>DSoundPlay</c>, <c>0x004402F4</c>)로 <c>Waves.K2</c> 의 WAV 를 튼다. 소리 번호 12 ~ 21 이
/// 묶음의 0 ~ 9 번이다(<c>0x00440600</c> → 소리 표 <c>0x0049F7D8</c>, 곡과 같은 표의 WAV 칸). 자산 <c>sound/NN.wav</c> 를 쓴다.
/// </summary>
public static class Sound
{
    /// <summary>
    /// 0 · 1 해전(<c>0x0040E92A</c> · <c>0x0040F426</c>) · 3 <c>0x00414305</c> · 4 폭풍(<c>0x00419244</c>) · 5 괴물(<c>0x00447FAB</c>) ·
    /// 6 상륙(<c>0x0041BC31</c>) · 8 해전 시작 · 엔딩(<c>0x0040AE79</c> · <c>0x004043F9</c>) · 9 해전 이김(<c>0x0040AF2B</c>).
    /// </summary>
    public const int Cannon = 0, Melee = 1, Storm = 4, Monster = 5, Shore = 6, BattleStart = 8, Victory = 9;

    private static readonly List<MediaPlayer> _live = [];

    /// <summary>자산 폴더 — <c>sound</c> 가 그 아래에 있다. 없으면 소리를 내지 않는다.</summary>
    public static string? Folder { get; set; }

    public static bool Enabled { get; set; } = true;

    public static void Play(int wave)
    {
        if (!Enabled || Folder is null) return;
        string path = Path.Combine(Folder, "sound", $"{wave:00}.wav");
        if (!File.Exists(path)) return;
        var p = new MediaPlayer { Volume = 0.6 };
        p.MediaEnded += (_, _) => { p.Close(); _live.Remove(p); };
        _live.Add(p);   // 다 울릴 때까지 붙들어 둔다
        p.Open(new Uri(path));
        p.Play();
    }
}
