using System.IO;
using System.Text.Json;

namespace Uw2.Game.UI.Audio;

/// <summary>
/// 「환경」(목록 30 의 ＢＧＭ) 소리 켜고 끄기 — 세이브 곁(<c>%LOCALAPPDATA%/Uw2/audio.json</c>)에 둔다.
/// </summary>
public static class AudioSettings
{
    private sealed record Data(bool Music = true, bool Sound = true);

    private static string PathOf =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Uw2", "audio.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(PathOf)) return;
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(PathOf)) ?? new Data();
            Music.Enabled = d.Music;
            Sound.Enabled = d.Sound;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf)!);
            File.WriteAllText(PathOf, JsonSerializer.Serialize(new Data(Music.Enabled, Sound.Enabled)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
