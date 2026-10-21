namespace Uw2.Game.Engine.Town;

/// <summary>
/// 주점 「선원 고용」의 셈 — <c>KOUKAI2.EXE 0x004241B0</c>.
/// </summary>
/// <remarks>
/// <code>
///   빈 자리   배마다 최대 선원(배 기록 +0x12) − 지금 선원. 하나도 없으면 「더 태울 자리가 없다」(0x00424330)
///   돈        10 아래면 못 쓴다
///   매력 몫 S  주인공 능력치 +0x22(일곱째) / 3 (0x00426CAD). 30 아래면 원본은 한 번 더 묻는다
///   올 사람    min(항구 경제 x S / 500, (작위 + 1) x S) — 작위는 0 없음 · 1 사작 … 9 공작(0x00424390)
///   한 사람 값 항구 경제 / 20 + 5 (0x00424461)
///   쓸 수 있는 수  min(올 사람, 돈 / 값, 빈 자리 합)
/// </code>
/// 원본은 고용한 뒤 배마다 몇 명을 태울지 고르게 하고(0x00424540), 새로 탄 사람만큼 사기를 60 쪽으로 끌어당긴다
/// (<c>60 + (사기 − 60) x 전 선원 / 새 선원</c>). 여기서는 앞 배부터 빈 자리를 채우며 사기를 같은 식으로 셈한다(<see cref="PlayerState.Hire"/>).
/// </remarks>
public static class Pub
{
    public const int MinGold = 10;

    public static int Price(int economy) => economy / 20 + 5;

    public static int Available(int economy, int charm, int rank) => AvailableAt(economy, Mood(charm), rank);

    /// <summary>주점 분위기 S(<c>0x0049A1B0</c>) — 들어올 때 매력 / 3(<c>0x00426CBF</c>), 한턱내면 오른다.</summary>
    public static int Mood(int charm) => charm / 3;

    public static int AvailableAt(int economy, int mood, int rank) => Math.Max(0, Math.Min(economy * mood / 500, (rank + 1) * mood));

    /// <summary>분위기가 이보다 낮으면 고용 앞에 술을 더 내라고 한다(0x1F · 0x20).</summary>
    public const int LowMood = 30;

    /// <summary>
    /// 「한턱내기」(<c>0x00424880</c>) — 술 n 병을 내면 분위기 += 매력 x (n x 1000 / 경제) / 10, 100 까지.
    /// 한 병 값은 항구 형편 +0x24 갈래의 술값, 병은 50 까지(가진 돈 mod 10000 에 1만 넘으면 1만을 더한 것 / 값).
    /// </summary>
    public static int Treat(int mood, int bottles, int economy, int charm) =>
        Math.Min(100, mood + charm * (bottles * 1000 / Math.Max(1, economy)) / 10);

    public static int MostBottles(int gold, int price) =>
        price <= 0 ? 0 : Math.Min(50, (gold % 10000 + (gold >= 10000 ? 10000 : 0)) / price);

    /// <summary>술 이름 — 갈래마다(<c>0x00457148</c>, 11바이트씩).</summary>
    public static readonly string[] Drinks = ["럼주", "와인", "위스키", "브랜디", "진", "맥주", "보드카", "테킬라", "야자주", "유주"];

    /// <summary>
    /// 한턱낸 뒤의 항구 소문(<c>0x00424B70</c>) — 지금 항구 번호에서 rand(5) 만큼 앞이나 뒤(rand(2))의 항구가 0 ~ 99 안에 있고
    /// 아직 모르는 곳(표 12 +0x15 &amp; 0x10 이 0)이면 그 항구를 고른다. 열 번 해서 못 고르면 -1.
    /// </summary>
    public static int RumourPort(int current, Func<int, bool> known, Func<int, int> rand)
    {
        for (int tries = 0; tries < 10; tries++)
        {
            int off = rand(5);
            bool ahead = rand(2) != 0;
            int port = ahead ? current + off : current - off;
            if (port < 0 || port > 0x63) continue;
            if (!known(port)) return port;
        }
        return -1;
    }

    /// <summary>
    /// 노리는 나라 소문(<c>0x00424A80</c>) — rand(200) &lt; 주인공 운(+0x23) 이면 나라 0 ~ 6 을 차례로 보며 rand(3) ≠ 0 이고
    /// 나라 표 +3 이 3 이나 7 인 첫 나라를 「그건 그렇고, 당신, ○○(가) 노리고 있어.」(0x25 · 0x28)로 알린다. 없으면 -1.
    /// </summary>
    public static int Hunter(int luck, Func<int, int> nationField3, Func<int, int> rand)
    {
        if (rand(200) >= luck) return -1;
        for (int n = 0; n < 7; n++)
            if (rand(3) != 0 && nationField3(n) is 3 or 7) return n;
        return -1;
    }

    /// <summary>
    /// 소문 갈래(<c>0x00424AD5</c>) — rand(1300 − 12 x 병) 가 항구 경제보다 크면 항구 소문, 아니면 짐 팔 곳 귀띔. 술을 많이 낼수록 귀띔이 잦다.
    /// </summary>
    public static bool PortRumourFirst(int bottles, int economy, Func<int, int> rand) => rand(Math.Max(1, 0x514 - bottles * 12)) > economy;

    /// <summary>
    /// 짐 팔 곳 귀띔(<c>0x00424CB0</c>) — 제 배 칸 열(rand(10)) 가운데 하나의 짐 칸 다섯(rand(5)) 하나를 쉰 번까지 뽑아 빈 칸이 아닌 짐을 고른다.
    /// 고르면 시장 0 ~ 7 가운데 그 짐 값(+0 낱말)이 가장 비싼 곳 — 더 비싼 곳을 만날 때마다 rand(8) ≠ 0 이어야 바꾼다(<c>0x00424D30</c>).
    /// 짐이 없으면 (-1, -1) 로 항구 소문에 넘긴다.
    /// </summary>
    public static (int Good, int Market) CargoTip(IReadOnlyList<int[]> cargo, IReadOnlyList<int[]> pays, Func<int, int> rand)
    {
        int good = -1;
        for (int tries = 0; tries < 50 && good < 0; tries++)
        {
            int ship = rand(10), slot = rand(5);
            if (ship < cargo.Count && slot < cargo[ship].Length && cargo[ship][slot] >= 0) good = cargo[ship][slot];
        }
        if (good < 0) return (-1, -1);
        int best = 0, market = 0;
        for (int m = 0; m < 8 && m < pays.Count; m++)
            if (good < pays[m].Length && pays[m][good] > best && rand(8) != 0) { best = pays[m][good]; market = m; }
        return (good, market);
    }

    /// <summary>항구 자리를 도 단위 경위도로(<c>0x00424120</c>) — 말 0x7E/0x7F(북위 · 남위) 와 0x80/0x81(동경 · 서경).</summary>
    public static (bool North, int Lat, bool East, int Lon) Degrees(int x, int y)
    {
        int lon = (x + 0x7BD) % 0x870;
        bool west = lon > 0x438;
        if (west) lon = 0x870 - lon;
        int lat = 0x280 - y;
        return (lat >= 0, Math.Abs(lat) * 8 / 57, !west, lon / 6);
    }

    /// <summary>쓸 수 있는 수 — 올 사람 · 돈 · 빈 자리 가운데 작은 것.</summary>
    public static int MostHirable(int available, int gold, int price, int vacancy) =>
        Math.Max(0, Math.Min(available, Math.Min(price > 0 ? gold / price : 0, vacancy)));
}
