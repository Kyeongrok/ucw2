using System.IO;
using System.Text.Json;

namespace Uw2.Game.UI.Audio;

/// <summary>
/// 「환경」(목록 20, <c>0x00444CA0</c>) — 소리 켜고 끄기와 바다 빠르기 · 화면 따라가기. 세이브 곁(<c>%LOCALAPPDATA%/Uw2/audio.json</c>)에 둔다.
/// </summary>
/// <remarks>
/// 원본 환경은 다섯 줄이다(이름 표 <c>0x00461DB8</c>, 일본어 그대로): 洋上速度(<c>0x0045A1D8</c>, 0 ~ 10 · 처음 5) · 洋上移動値(<c>0x0045A1D5</c>, 0 ~ 10, 화면이 배를 따라
/// 미는 가장자리 — <see cref="SeaSlack"/>) · レイアウト(<c>0x0045A1E0</c> 비트 0x80 — 바다 명령 띠) · ＢＧＭ(비트 4) · 表示効果(비트 1 — 띠를 찍는 꾸밈).
/// 셋째 · 다섯째는 꾸밈이라 옮기지 않았다.
/// </remarks>
public static class AudioSettings
{
    private sealed record Data(bool Music = true, bool Sound = true, int SeaSpeed = DefaultSeaSpeed, int SeaSlack = 0);

    /// <summary>洋上速度의 처음 값과 끝.</summary>
    public const int DefaultSeaSpeed = 5, MaxSeaSpeed = 10;

    /// <summary>바다 빠르기 — 한 틱에 <c>(값 + 1) x 20</c> 밀리초를 기다린다(<see cref="Uw2.Game.Engine.Sea.Fleet.SpeedSetting"/>). 작을수록 빠르다.</summary>
    public static int SeaSpeed { get; set; } = DefaultSeaSpeed;

    /// <summary>洋上移動値의 끝.</summary>
    public const int MaxSeaSlack = 10;

    /// <summary>
    /// 바다 화면이 배를 따라가기 전 배가 가운데서 벗어날 수 있는 몫 — 원본은 화면 왼 · 위 끝에서 배까지가 <c>11 - 값</c> 보다 작거나
    /// <c>11 + 값</c> 보다 크면 한 칸 민다(<c>0x00448AA1</c> · <c>0x00448ADD</c>). 0 이면 늘 가운데, 처음 값도 0 이다.
    /// </summary>
    public static int SeaSlack { get; set; }

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
            SeaSpeed = Math.Clamp(d.SeaSpeed, 0, MaxSeaSpeed);
            SeaSlack = Math.Clamp(d.SeaSlack, 0, MaxSeaSlack);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf)!);
            File.WriteAllText(PathOf, JsonSerializer.Serialize(new Data(Music.Enabled, Sound.Enabled, SeaSpeed, SeaSlack)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
