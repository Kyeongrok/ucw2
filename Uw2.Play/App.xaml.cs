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
        new SeaMapWindow { Title = "대항해시대2 — 항해" }.Show();
    }
}
