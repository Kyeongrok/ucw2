using System.Windows;
using Uw2.Game.UI.Views;

namespace Uw2.Play;

/// <summary>
/// 놀이 껍데기. 뷰어(<c>Uw2.exe</c>)와 나란히 놓이므로 이름을 갈라 뒀다
/// (<c>Uw2Play.exe</c>). 3편 <c>CdsHelper.Play</c> 자리다.
/// </summary>
/// <remarks>
/// 아직 지도만 띄운다. 배가 움직이려면 항해 속도식과 통행 판정이 먼저다 —
/// 볼트 <c>Project/uw2/분석/1.분석-해상이동 자산 한눈에</c> 의 "남은 것" 을 볼 것.
/// </remarks>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (e.Args.Contains("--battle-demo")) { BattleWindow.Demo(); Shutdown(); return; }
        ShowTitle();
    }

    /// <summary>제목 창 — 게임이 끝나면(<see cref="SeaMapWindow.Ended"/>) 다시 띄운다.</summary>
    private void ShowTitle()
    {
        var title = new TitleWindow();
        bool started = false;
        SeaMapWindow Open()
        {
            started = true;
            var map = new SeaMapWindow { Title = "대항해시대2 — 항해" };
            map.Ended += ShowTitle;
            map.Closed += (_, _) => { if (Windows.Count == 0) Shutdown(); };
            map.Show();
            return map;
        }
        title.HeroChosen += hero => { Open().ShowHomePort(hero); title.Close(); };
        title.SaveChosen += save => { Open().LoadGame(save); title.Close(); };
        title.Closed += (_, _) => { if (!started && Windows.Count == 0) Shutdown(); };
        title.Show();
    }
}
