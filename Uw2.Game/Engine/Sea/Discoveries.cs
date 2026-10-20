using Uw2.Support.Local.Formats;

namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 발견물 백(98 개 씀) — 시나리오 기억 표 16(<c>0x00460620</c>, <c>SDATA.DAT 0x5D86</c>), 7바이트씩.
/// </summary>
/// <remarks>
/// <code>
///   +0 x · +2 y (칩 눈금 — 항구 · 함대와 같은 자)   +4 이어진 아이템(0x5A~)   +5 어려움
///   +6 깃발   &amp;7 갈래(보물 · 원주민 · 동물 · 식물 · 유적 · 괴물 · 자연 · 원주민)
///             0x20 찾았다   0x80 이번 게임에는 없다
/// </code>
/// 새 게임(<c>0x00416AE0</c>)은 98 개에 다 0x80 을 세우고 rand(98) 로 쉰 개를 골라 0x80 을 푼다.
/// 남은 것 가운데 열 개는 아이템 0x50~0x59(+0x12 에 발견물 번호)에 묶는데, 그건 아직 안 한다.
///
/// 상륙(<c>0x0041AD60</c> → <c>0x0041AFD0</c>)은 내린 칩과 그 왼쪽 · 위 칩에서 발견물을 찾는다. 있으면 「부락에 상륙하겠습니다」 뒤
/// 목록 35(보물탐색 · 환대 · 식량약탈 · 출항 · 대기, <c>0x0041BA90</c>), 없으면 목록 36(출항 · 응급수리 · 탐색 · 대기, <c>0x0041B090</c>).
/// </remarks>
public static class Discoveries
{
    public const int Table = 16, Size = 7, Count = 100, Used = 98, InGame = 50;
    public const int Linked = 4, Difficulty = 5, Flags = 6;
    public const int Found = 0x20, Absent = 0x80;
    public const int Treasure = 0, Natives = 1, Monster = 5;

    /// <summary>갈래 이름(<c>0x00457560</c>, 7바이트씩).</summary>
    public static readonly string[] KindNames = ["보물", "원주민", "동물", "식물", "유적", "괴물", "자연", "원주민"];

    public readonly record struct Spot(int Index, int X, int Y, int Difficulty, int Flags)
    {
        public int Kind => Flags & 7;
        public bool IsFound => (Flags & Found) != 0;
        public bool IsAbsent => (Flags & Absent) != 0;
        public bool Hostile => Kind is Natives or Monster;
    }

    private static ScenarioMemory.Pointer At(int i, int field) => ScenarioMemory.At(Table, i, field);

    public static Spot Get(ScenarioMemory m, int i) => new(i, (short)m.Read(At(i, 0), true), (short)m.Read(At(i, 2), true),
                                                          m.Read(At(i, Difficulty), false), m.Read(At(i, Flags), false));

    /// <summary>새 게임 — 쉰 개만 이번 게임에 둔다(<c>0x00416AE0</c>).</summary>
    public static void Seed(ScenarioMemory m, Func<int, int> rand)
    {
        for (int i = 0; i < Used; i++) m.Write(At(i, Flags), m.Read(At(i, Flags), false) | Absent, false);
        for (int placed = 0; placed < InGame;)
        {
            int i = rand(Used);
            int f = m.Read(At(i, Flags), false);
            if ((f & Absent) == 0) continue;
            m.Write(At(i, Flags), f & ~Absent, false);
            placed++;
        }
    }

    /// <summary>
    /// 함대(칩 2 x 2, 왼위가 <paramref name="x"/> · <paramref name="y"/>) 곁의 발견물 — 없으면 null.
    /// 원본은 방향을 골라 내린 칩 하나를 보지만, 우리는 함대 둘레 <paramref name="reach"/> 칩 안에서 가장 가까운 것을 잡는다.
    /// </summary>
    public static Spot? Near(ScenarioMemory m, int x, int y, int reach = 3)
    {
        Spot? best = null;
        int bestD = int.MaxValue;
        for (int i = 0; i < Used; i++)
        {
            var s = Get(m, i);
            if (s.IsAbsent) continue;
            int dx = Gap(s.X, x), dy = Gap(s.Y, y);
            int d = Math.Max(dx, dy);
            if (d <= reach && d < bestD) { best = s; bestD = d; }
        }
        return best;

        // 함대가 차지한 두 칩(x, x+1)에서 떨어진 거리 — 지도는 옆으로 돈다.
        static int Gap(int a, int lo)
        {
            int w = ChipMap.Width;
            int d = ((a - lo) % w + w) % w;
            if (d > w / 2) d -= w;
            return d < 0 ? -d : Math.Max(0, d - 1);
        }
    }

    public enum Outcome { NoFood, AlreadyFound, Nothing, Found, Attacked }

    /// <summary>
    /// 「보물탐색」(<c>0x0041BB50</c>) — 식량이 있어야 한다(0x1AC). 이미 찾았으면 0x1B0, 어려움(+5) ≥ 운 x 3/20 + 지식(+0x1F) x 7/20 + 환대 덤이면
    /// 0x1AD. 아니면 찾는다 — 원주민 · 괴물이면 습격(0x1AE)부터 받는다. 찾은 것에는 0x20 을 세운다.
    /// </summary>
    public static Outcome Search(ScenarioMemory m, int index, int luck, int knowledge, int bonus, bool hasFood)
    {
        if (!hasFood) return Outcome.NoFood;
        var s = Get(m, index);
        if (s.IsFound) return Outcome.AlreadyFound;
        if (s.Difficulty >= (byte)(luck * 3 / 20 + knowledge * 7 / 20 + bonus)) return Outcome.Nothing;
        m.Write(At(index, Flags), s.Flags | Found, false);
        return s.Hostile ? Outcome.Attacked : Outcome.Found;
    }

    /// <summary>
    /// 땅에서 싸운 세기(<c>0x0041BC88</c>) — v = min(100, 검술/2 x 전투 레벨 / 25 + 검술/2), 세기 = (100 − v) / 2.
    /// 배마다 선원 x 세기 / 100 이 죽는다(<c>0x0041ACE0</c>, 말 0x1AB).
    /// </summary>
    public static int FightStrength(int swordsmanship, int battleLevel)
    {
        int half = swordsmanship / 2;
        int v = Math.Min(100, battleLevel * half / 25 + half);
        return (100 - v) / 2;
    }

    /// <summary>「환대」 — 식량 20 을 내주고 탐색 덤이 5 오른다(<c>0x0041BD40</c>, 말 0x1B2).</summary>
    public const int FeastFood = 20, FeastBonus = 5;

    /// <summary>「식량약탈」 — (rand(5) + 2) x 10 을 빼앗는다. 넷에 하나는 반격을 받는다(<c>0x0041BE00</c>).</summary>
    public static int LootAmount(Func<int, int> rand) => (rand(5) + 2) * 10;
}
