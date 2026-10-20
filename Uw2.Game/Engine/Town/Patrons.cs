using System.Text;
using Uw2.Game.Engine.Sea;

namespace Uw2.Game.Engine.Town;

/// <summary>
/// 「NPC」 건물의 임자 스물 — 시나리오 기억 표 4(<c>0x0045B9B0</c>, <c>SDATA.DAT 0xAF7</c>), 22바이트씩.
/// </summary>
/// <remarks>
/// <code>
///   +0    이름 17(cp949)
///   +0x11 갈래   0 발견물을 사는 귀족(목록 24, 0x00421E90) · 1 학자(목록 25) · 0xFF 왕 · 2 · 3
///   +0x12 얼굴(낱말)
///   +0x14 깃발   &amp;3 집안 격(0 이 높다) · 0x10 계약했다
///   +0x15 사는 항구
/// </code>
/// 귀족에게 「발견물 보고」(<c>0x00421B30</c>)를 하면, 찾았고(0x20) 이번 게임에 있고 아직 안 판(0x10) 발견물을 하나 골라
/// <c>값 = (5 − 격) x 어려움 x 50</c>(어려움 100 이면 x 4)을 받고, 주인공 표 +4(모험가 명성)가
/// <c>min(50000 − 명성, 어려움 x 8)</c>(100 이면 x 15) 오른다. 말은 <c>0x1E6 + 어려움 / 25</c> 다.
/// </remarks>
public static class Patrons
{
    public const int Table = 4, Size = 22, Count = 20;
    public const int Kind = 0x11, Face = 0x12, Flags = 0x14, Port = 0x15;
    public const int Noble = 0, Scholar = 1, Contracted = 0x10, Reported = 0x10;
    public const int FameField = 4, MaxFame = 50000;

    private static ScenarioMemory.Pointer At(int i, int field) => ScenarioMemory.At(Table, i, field);

    /// <summary>그 항구에 사는 임자 — 없으면 -1.</summary>
    public static int InPort(ScenarioMemory m, int port)
    {
        for (int i = 0; i < Count; i++)
            if (m.Read(At(i, Kind), false) != 0xFF && m.Read(At(i, Port), false) == port) return i;
        return -1;
    }

    public static string Name(ScenarioMemory m, int i)
    {
        var raw = new byte[Kind];
        for (int k = 0; k < Kind; k++) raw[k] = (byte)m.Read(At(i, k), false);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949).GetString(raw).Split('\0')[0].Trim();
    }

    public static int KindOf(ScenarioMemory m, int i) => m.Read(At(i, Kind), false);

    public static bool IsContracted(ScenarioMemory m, int i) => (m.Read(At(i, Flags), false) & Contracted) != 0;

    /// <summary>「계약」(<c>0x00421AA0</c>) — 다른 귀족과의 계약은 풀린다.</summary>
    public static void Contract(ScenarioMemory m, int i)
    {
        for (int k = 0; k < Count; k++)
            if (m.Read(At(k, Kind), false) == Noble)
                m.Write(At(k, Flags), m.Read(At(k, Flags), false) & ~Contracted, false);
        m.Write(At(i, Flags), m.Read(At(i, Flags), false) | Contracted, false);
    }

    /// <summary>팔 수 있는 발견물 — 찾았고 이번 게임에 있고 아직 안 판 것.</summary>
    public static List<int> Reportable(ScenarioMemory m)
    {
        var list = new List<int>();
        for (int i = 0; i < Discoveries.Count; i++)
        {
            int f = Discoveries.Get(m, i).Flags;
            if ((f & Discoveries.Absent) == 0 && (f & Discoveries.Found) != 0 && (f & Reported) == 0) list.Add(i);
        }
        return list;
    }

    public static int Reward(int rank, int difficulty) => (5 - (rank & 3)) * (difficulty == 100 ? 4 : 1) * difficulty * 50;

    public static int FameGain(int fame, int difficulty) => Math.Min(MaxFame - fame, difficulty * (difficulty == 100 ? 15 : 8));

    /// <summary>
    /// 발견물 하나를 판다 — 값을 돌려주고(돈은 부르는 쪽이 더한다) 모험가 명성을 올리고 0x10 을 세운다.
    /// <paramref name="message"/> 는 쓸 말 번호다.
    /// </summary>
    public static int Report(ScenarioMemory m, int patron, int discovery, int hero, out int fame, out int message)
    {
        var s = Discoveries.Get(m, discovery);
        int reward = Reward(m.Read(At(patron, Flags), false), s.Difficulty);
        var fp = ScenarioMemory.At(ScenarioMemory.HeroTable, hero, FameField);
        int before = m.Read(fp, true);
        fame = FameGain(before, s.Difficulty);
        m.Write(fp, before + fame, true);
        m.Write(ScenarioMemory.At(Discoveries.Table, discovery, Discoveries.Flags), s.Flags | Reported, false);
        message = 0x1E6 + s.Difficulty / 25;
        return reward;
    }
}
