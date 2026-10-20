namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 바다에서 항구 · 부락을 알아보기 — <c>KOUKAI2.EXE 0x00447C2D</c>.
/// </summary>
/// <remarks>
/// <code>
///   눈       주인공과 동료 가운데 가장 높은 지식(사람 +0x1F)
///   항구 130  기록 +0x15 &amp; 0x30 이 0 이면(아직 모른다) — 함대와 가로 · 세로 2 칩 안이면 알아보고,
///            보이는 거리(<c>0x00448030</c>, 안개면 0) 안이면 rand(200) &lt; 눈일 때 알아본다. +0x15 |= 0x10
///   발견물    +6 &amp; 0xC0 이 0 이면 같은 셈으로 알아본다. +6 |= 0x40
///   명성     항구 100 아래는 항구 형편 +0x1C x 25, 그 밖(특별한 곳) · 부락은 50 — 주인공 표 +4, 50000 까지(말 0x1DC)
/// </code>
/// 보이는 거리는 min(12, 배마다 (칸 곁 +8 몫% x 선원) 가운데 가장 큰 것 / 100) 에 망원경(아이템 0x18)이면 x 2 다
/// (<c>0x004355C8</c> · <c>0x0043568D</c>, <see cref="Reach"/>).
/// </remarks>
public static class Sighting
{
    public const int PortCount = 130, PortTable = ScenarioMemory.PortTable, PortFlags = 0x15, Known = 0x10, Seen = 0x40;
    public const int SureReach = 2, MaxSight = 12, Telescope = 0x18;

    /// <summary>보이는 거리(칩) — 배마다 (몫% x 선원) 의 가장 큰 것 / 100 을 12 에서 자르고 망원경이면 두 배.</summary>
    public static int Reach(IEnumerable<(int Share, int Crew)> ships, bool telescope) =>
        Math.Min(MaxSight, ships.Select(s => s.Share * s.Crew).DefaultIfEmpty(0).Max() / 100) * (telescope ? 2 : 1);

    public readonly record struct Sight(bool Port, int Index, int Fame);

    /// <summary>
    /// 함대 자리(칩 <paramref name="fx"/> · <paramref name="fy"/>)에서 새로 알아본 것들. 깃발은 바로 세운다.
    /// <paramref name="portFame"/> 는 항구마다 형편 +0x1C(명성 값)다.
    /// </summary>
    public static List<Sight> Look(ScenarioMemory m, int fx, int fy, int eye, int sight, Func<int, int> rand, Func<int, int> portFame)
    {
        var found = new List<Sight>();
        bool Near(int x, int y, int r) => Math.Abs(x - fx) <= r && Math.Abs(y - fy) <= r;
        bool Spot(int x, int y) => Near(x, y, SureReach) || (sight > 0 && Near(x, y, sight) && rand(200) < eye);

        for (int p = 0; p < PortCount; p++)
        {
            var flags = ScenarioMemory.At(PortTable, p, PortFlags);
            int f = m.Read(flags, false);
            if ((f & 0x30) != 0) continue;
            int x = (short)m.Read(ScenarioMemory.At(PortTable, p, 0), true), y = (short)m.Read(ScenarioMemory.At(PortTable, p, 2), true);
            if (x == 0 && y == 0) continue;
            if (!Spot(x, y)) continue;
            m.Write(flags, f | Known, false);
            found.Add(new Sight(true, p, p < 100 ? portFame(p) * 25 : 50));
        }
        for (int i = 0; i < Discoveries.Used; i++)
        {
            var d = Discoveries.Get(m, i);
            if ((d.Flags & 0xC0) != 0 || !Spot(d.X, d.Y)) continue;
            m.Write(ScenarioMemory.At(Discoveries.Table, i, Discoveries.Flags), d.Flags | Seen, false);
            found.Add(new Sight(false, i, 50));
        }
        return found;
    }

    /// <summary>명성을 올린다 — 주인공 표 +4, 50000 까지. 오른 만큼을 돌려준다.</summary>
    public static int AddFame(ScenarioMemory m, int hero, int fame)
    {
        var at = ScenarioMemory.At(ScenarioMemory.HeroTable, hero, Town.Patrons.FameField);
        int before = m.Read(at, true);
        int gain = Math.Min(Town.Patrons.MaxFame - before, fame);
        m.Write(at, before + gain, true);
        return gain;
    }
}
