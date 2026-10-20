using Uw2.Game.Engine;
using Uw2.Support.Local.Formats;
using Xunit;

namespace Uw2.Tests;

/// <summary>주인공 시나리오 여섯을 새 게임 상태로 모든 항구 · 건물에 들여 본다.</summary>
public class ScenarioSurveyTests
{
    private static string SnrDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, AssetPack.FolderName))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? ".", AssetPack.FolderName, "snr");
        }
    }

    /// <summary>시나리오 n(1~6)의 첫 절에서 이벤트가 걸린 (항구, 건물)마다 한 번 돌린다.</summary>
    private static IEnumerable<(int Port, int Building, ScenarioVm.Run Run)> Survey(int n)
    {
        var code = File.ReadAllBytes(Path.Combine(SnrDir, $"SNR{n}.DAT"));
        var text = ScenarioText.Load(Path.Combine(SnrDir, $"SNR{n}.MES"));
        for (int port = 0; port < 0x82; port++)
            for (int b = 0; b < 12; b++)
            {
                // 깃발이 남지 않게 매번 새 기계로.
                var vm = new ScenarioVm(code, text);
                if (vm.Enter(port, b, 1000, "이름", "성") is { } run) yield return (port, b, run);
            }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EveryHeroHasAnOpeningEventThatEnds(int n)
    {
        if (!Directory.Exists(SnrDir)) return;
        var hits = Survey(n).ToList();
        Assert.NotEmpty(hits);
        // 이벤트가 그침 없이 걸음 한도까지 돌지 않는다 — 대사가 한도 근처로 쌓이면 빙빙 도는 것이다.
        Assert.All(hits, h => Assert.True(h.Run.Lines.Count < 200, $"{n}: 항구 {h.Port} 건물 {h.Building} 대사 {h.Run.Lines.Count}"));
        Assert.Contains(hits, h => h.Run.Lines.Count > 0);
        string? dump = Environment.GetEnvironmentVariable("UW2_SURVEY");
        if (dump != null)
            File.AppendAllLines(dump, hits.Select(h =>
                $"SNR{n} 항구 {h.Port,3} 건물 {h.Building,2} 대사 {h.Run.Lines.Count,3} 배 [{string.Join(",", h.Run.Ships)}] 돈 {h.Run.Gold}"
                + (h.Run.Lines.Count > 0 ? $"  「{h.Run.Lines[0].Body.Replace('\n', ' ')[..Math.Min(30, h.Run.Lines[0].Body.Length)]}」" : "")));
    }
}
