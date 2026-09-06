using System.Windows;
using Uw2.Game.UI.Views;

namespace Uw2;

/// <summary>
/// 보기 껍데기. 지금은 세계지도 창 하나뿐이다 — 3편 <c>CdsHelper</c> 자리다.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new SeaMapWindow().Show();
    }
}
