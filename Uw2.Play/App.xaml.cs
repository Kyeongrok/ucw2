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
        // 배경 음악은 원본 폴더의 bgm/TrackNN.mp3 — 개발 시험(--…-smoke)은 소리 없이.
        if (!e.Args.Any(x => x.EndsWith("-smoke") || x.EndsWith("-shot"))) { Uw2.Game.UI.Audio.Music.Folder = Uw2.Support.Local.Helpers.GameFolder.Find(); Uw2.Game.UI.Audio.Sound.Folder = Uw2.Support.Local.Formats.AssetPack.FindFolder(); Uw2.Game.UI.Audio.AudioSettings.Load(); }
        if (e.Args.Contains("--battle-demo")) { BattleWindow.Demo(); Shutdown(); return; }
        if (Array.IndexOf(e.Args, "--duel-shot") is >= 0 and var ds && ds + 1 < e.Args.Length
            && Uw2.Support.Local.Formats.AssetPack.FindFolder() is { } duelAssets)
        {
            // 개발 — 일기토 화면을 화면 밖에 그려 그림으로 적는다.
            var graphs = new GraphSheet(Uw2.Support.Local.Formats.AssetPack.PicturesOnly(duelAssets));
            DuelWindow.Sprites = (hero, variant, group, frame) => graphs.DuelFrame(hero ? "IAP1" : "IAE1", variant, group, frame);
            DuelWindow.Arena = graphs.Get(24);
            DuelWindow.Backdrop = graphs.Get(23);
            DuelWindow.DevShot(e.Args[ds + 1]);
            Shutdown();
            return;
        }
        if (Array.IndexOf(e.Args, "--battle-smoke") is >= 0 and var at && at + 1 < e.Args.Length)
        {
            // 개발 — 새 게임 바다에서 남 함대와 한 판을 돌려 보고 끝낸다.
            var map = new SeaMapWindow { Title = "대항해시대2 — 해전 시험" };
            map.Show();
            map.ShowHomePort(0);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                map.DevBattleSmoke(e.Args[at + 1], at + 2 < e.Args.Length ? e.Args[at + 2] : null);
                Shutdown();
            };
            t.Start();
            return;
        }
        if (Array.IndexOf(e.Args, "--autopilot-smoke") is >= 0 and var ap && ap + 1 < e.Args.Length)
        {
            var map = new SeaMapWindow { Title = "대항해시대2 — 항이동 시험" };
            map.Show();
            map.ShowHomePort(0);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) => { t.Stop(); map.DevAutopilotSmoke(e.Args[ap + 1]); Shutdown(); };
            t.Start();
            return;
        }
        if (Array.IndexOf(e.Args, "--portmap-demo") >= 0)
        {
            // 개발 — 첫 주인공 고향 항구의 항구맵을 띄운다.
            var map = new SeaMapWindow { Title = "대항해시대2 — 항구맵 시험" };
            map.Show();
            map.ShowHomePort(0);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            int pm = Array.IndexOf(e.Args, "--portmap-demo");
            string? shot = pm + 1 < e.Args.Length ? e.Args[pm + 1] : null;
            t.Tick += (_, _) => { t.Stop(); map.DevPortMap(shot); if (shot != null) Shutdown(); };
            t.Start();
            return;
        }
        if (Array.IndexOf(e.Args, "--sea-demo") >= 0)
        {
            // 개발 — 첫 주인공 고향 앞바다에 배를 띄운다(바다 배 그림 · 남 함대 보기).
            var map = new SeaMapWindow { Title = "대항해시대2 — 바다 시험" };
            map.Show();
            map.ShowHomePort(0);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) => { t.Stop(); map.DevPutToSea(); };
            t.Start();
            return;
        }
        if (Array.IndexOf(e.Args, "--town-demo") >= 0)
        {
            // 개발 — 첫 주인공 고향 항구를 바로 띄운다(마을 사람 보기).
            var map = new SeaMapWindow { Title = "대항해시대2 — 도시 시험", WindowState = WindowState.Maximized };
            map.Show();
            int td = Array.IndexOf(e.Args, "--town-demo");
            map.ShowHomePort(td + 1 < e.Args.Length && int.TryParse(e.Args[td + 1], out int hero) ? hero : 0);
            return;
        }
        if (Array.IndexOf(e.Args, "--import-smoke") is >= 0 and var ia && ia + 2 < e.Args.Length)
        {
            // 개발 — 원본 세이브 칸마다 들여 보고 적은 뒤 끝낸다.
            var map = new SeaMapWindow { Title = "대항해시대2 — 원본 세이브 시험" };
            map.Show();
            map.ShowHomePort(0);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                map.DevImportSmoke(e.Args[ia + 1], e.Args[ia + 2]);
                Shutdown();
            };
            t.Start();
            return;
        }
        // 여는 화면(0x00431930) — 아무 글쇠나 누르면 건너뛴다. --no-opening 이면 안 띄운다.
        if (!e.Args.Contains("--no-opening") && Uw2.Support.Local.Formats.AssetPack.FindFolder() is { } assets)
        {
            Uw2.Game.UI.Audio.Music.Play(Uw2.Game.UI.Audio.Music.Opening);
            OpeningWindow.Play(new GraphSheet(Uw2.Support.Local.Formats.AssetPack.PicturesOnly(assets)));
        }
        ShowTitle();
    }

    /// <summary>제목 창 — 게임이 끝나면(<see cref="SeaMapWindow.Ended"/>) 다시 띄운다.</summary>
    private void ShowTitle()
    {
        Uw2.Game.UI.Audio.Music.Play(Uw2.Game.UI.Audio.Music.Title);
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
        title.HeroChosen += hero => { var map = Open(); map.ShowHomePort(hero); title.Close(); map.AskHeroName(); };
        title.SaveChosen += save => { Open().LoadGame(save); title.Close(); };
        title.Closed += (_, _) => { if (!started && Windows.Count == 0) Shutdown(); };
        title.Show();
    }
}
