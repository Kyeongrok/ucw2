namespace Uw2.Game.Engine;

/// <summary>
/// 게임 날짜 — 해 · 달 · 날을 게임처럼 <b>0 부터</b> 든다(<c>0x004703D8</c> · <c>0x004703DC</c> · <c>0x004703E0</c>).
/// </summary>
/// <remarks>
/// 게임의 맨 바깥 고리(<c>0x0043A9C9</c>)가 해 → 달 → 날로 돌고, 날마다 <c>0x00418D30</c> 이 하루를 논다.
/// <code>
///   달의 날 수   0x00459EB8 = 31 28 31 30 31 30 31 31 30 31 30 31   (0x0043AAD0)
///   윤달        2월이고 0x0043AB10 이 참이면 +1
///   해 넘김      0x0043A880 — 해 +1, 달 0. 해가 52 를 넘으면 게임이 끝난다. 사람 백스물의 나이(+0x2A)가 1 오른다
///   찍기        해는 1500 + 해 + 1, 날은 날 + 1, 달은 이름표 0x00459C38 (Jan ~ Dec)
/// </code>
/// <b>윤년 셈이 뒤집혀 있다</b> — <c>0x0043AB10</c> 은 <c>(1500 + 해)</c> 가 4 로 나뉘고 <b>100 으로도 나뉘며</b>
/// 400 으로는 안 나뉠 때만 참이다. 1521 ~ 1552 에는 그런 해가 없어 <b>2월은 늘 28일</b>이다. 그대로 옮긴다.
///
/// 새 게임은 <c>SDATA.DAT</c> 머리 +5 · +6 · +7 = <c>21 · 4 · 16</c> — 「1522년 May 17일」이다.
/// </remarks>
public struct GameDate
{
    private static readonly byte[] MonthDays = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>해 넘김에서 이보다 크면 게임이 끝난다.</summary>
    public const int LastYear = 52;

    public int Year, Month, Day;

    public GameDate(int year, int month, int day) { Year = year; Month = month; Day = day; }

    /// <summary>새 게임 날짜.</summary>
    public static GameDate NewGame => new(21, 4, 16);

    /// <summary><c>0x0043AB10</c> 그대로(뒤집힌 셈).</summary>
    public static bool OddLeap(int year)
    {
        int y = 1500 + year;
        return y % 4 == 0 && y % 100 == 0 && y % 400 != 0;
    }

    public static int DaysIn(int year, int month) =>
        MonthDays[month] + (month == 1 && OddLeap(year) ? 1 : 0);

    /// <summary>하루 넘긴다. 해가 <see cref="LastYear"/> 를 넘었으면 참(게임 끝).</summary>
    public bool NextDay()
    {
        if (++Day < DaysIn(Year, Month)) return false;
        Day = 0;
        if (++Month < 12) return false;
        Month = 0;
        return ++Year > LastYear;
    }

    public override readonly string ToString() => $"{1500 + Year + 1}년 {MonthNames[Month]} {Day + 1}일";
}
