using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// <c>MENU.DAT</c> — 고르기 목록 예순하나. 머리가 목록 자리(빅엔디언 낱말)들이고, 목록마다 <c>[칸 폭][개수]</c> 뒤에
/// 글이 0 으로 끊겨 이어진다.
/// </summary>
/// <remarks>
/// 고르기 손 <c>KOUKAI2.EXE 0x0042AB10(번호, …)</c> 이 목록 번호를 받는다. 건물 손이 넘기는 번호(<see cref="BuildingList"/>)는
/// 교역소 6 · 주점 15 · 조선소 22 · 항 9 · 여관 4 · 왕궁 16 · 조합 1 · 은행 0 · ITEM 5 · 교회 7 · 점술가 17 이다(NPC 는 없다).
/// </remarks>
public sealed class MenuTable
{
    /// <summary>건물 번호(<see cref="PortDoors.Names"/>) → 목록 번호. -1 은 목록이 없는 건물.</summary>
    public static ReadOnlySpan<sbyte> BuildingList => [6, 15, 22, 9, 4, 16, 1, -1, 0, 5, 7, 17];

    public IReadOnlyList<IReadOnlyList<string>> Lists { get; }

    private MenuTable(IReadOnlyList<IReadOnlyList<string>> lists) => Lists = lists;

    public static MenuTable FromLists(IReadOnlyList<IReadOnlyList<string>> lists) => new(lists);

    public static MenuTable Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static MenuTable FromBytes(byte[] d)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        int n = (d[0] << 8 | d[1]) / 2;
        var lists = new List<IReadOnlyList<string>>();
        for (int k = 0; k < n; k++)
        {
            int o = d[k * 2] << 8 | d[k * 2 + 1];
            var items = new List<string>();
            if (o + 2 <= d.Length)
            {
                int count = d[o + 1], at = o + 2;
                for (int i = 0; i < count && at < d.Length; i++)
                {
                    int end = Array.IndexOf(d, (byte)0, at);
                    if (end < 0) end = d.Length;
                    items.Add(cp949.GetString(d, at, end - at).Trim());
                    at = end + 1;
                }
            }
            lists.Add(items);
        }
        return new MenuTable(lists);
    }

    /// <summary>그 건물의 고르기 목록. 없으면 빈 것.</summary>
    public IReadOnlyList<string> ForBuilding(int building)
    {
        if (building < 0 || building >= BuildingList.Length) return [];
        int k = BuildingList[building];
        return k >= 0 && k < Lists.Count ? Lists[k] : [];
    }
}
