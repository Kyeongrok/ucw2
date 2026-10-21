namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 일기토(<c>KOUKAI2.EXE 0x00414FC0</c> → <c>0x004130E0</c>) — 두 제독이 스무 차례까지 공격과 방어를 번갈아 고른다.
/// </summary>
/// <remarks>
/// 줄 하나(<c>0x004715AC</c>, 0 ~ 200, 처음 100)를 당긴다. 짝수 차례는 건 쪽(<c>0x00499D74</c>)이 치고 줄을 늘리고,
/// 홀수 차례는 받은 쪽(<c>0x00499D78</c>)이 치고 줄을 줄인다(<c>0x00413C00</c>). 200 이면 건 쪽, 0 이면 받은 쪽이 이긴다.
/// 스무 차례가 지나도 안 끝나면 줄은 그대로 두고 싸움으로 돌아간다. 줄은 같은 상대와 다시 맞붙을 때 이어진다(<c>0x004715B0</c>).
/// <code>
///   공격  0 찌르기 · 1 베기 · 2 강타        방어  0 쳐내기 · 1 받기 · 2 피하기
/// </code>
/// </remarks>
public sealed class Duel
{
    public const int Turns = 20, Full = 200;

    /// <summary>
    /// 한 사람의 채비(<c>0x0049A128</c>, 12 바이트 x 2) — 무기 갈래(아이템 갈래 &amp; 0xF, 1 ~ 3) · 무기 세기 · 갑옷 세기(갈래 6 의 합).
    /// </summary>
    public sealed record Fighter(string Name, int Sword, int BattleLevel, int Style, int Weapon, int Armor, int Person = -1);

    private readonly Func<int, int> _rand;

    /// <summary>[0] 건 쪽 · [1] 받은 쪽.</summary>
    public Fighter[] Sides { get; }

    /// <summary>고른 것의 쌓임(<c>0x00499D80</c> · <c>0x00499D84</c>) — 지난 것 &lt;&lt; 2 | 이번 것.</summary>
    private readonly int[] _history = [1, 1];

    public int Turn { get; private set; }

    /// <summary>줄 — 200 이면 건 쪽이 이겼다.</summary>
    public int Rope { get; private set; }

    public Duel(Fighter challenger, Fighter defender, int rope, Func<int, int> rand)
    {
        Sides = [challenger, defender];
        Rope = rope;
        _rand = rand;
    }

    /// <summary>이번 차례에 치는 쪽(0 건 쪽 · 1 받은 쪽).</summary>
    public int Attacker => Turn & 1;

    public bool Over => Rope is 0 or Full || Turn >= Turns;

    /// <summary>
    /// 남의 고르기(<c>0x004132E0</c>) — rand(10) 를 두 문턱으로 가른다. 칠 때는 무기 갈래(1: 6 · 7, 2: 2 · 7, 3: 2 · 3, 없음: 3 · 6),
    /// 막을 때는 갑옷(60 이상 6 · 7, 20 이상 2 · 7, 그 아래 2 · 3)이 문턱이다. 첫 문턱 아래면 0, 둘째 넘으면 2, 그 사이 1.
    /// </summary>
    public int AiChoice(int side)
    {
        var f = Sides[side];
        int lo, hi;
        if (side == Attacker)
            (lo, hi) = f.Style switch { 1 => (6, 7), 2 => (2, 7), 3 => (2, 3), _ => (3, 6) };
        else
            (lo, hi) = f.Armor >= 60 ? (6, 7) : f.Armor >= 20 ? (2, 7) : (2, 3);
        int r = _rand(10);
        return r > hi ? 2 : r >= lo ? 1 : 0;
    }

    /// <summary>
    /// 한 차례(<c>0x004143F0</c> · <c>0x00413C00</c>) — 치는 쪽 고르기 <paramref name="attack"/>, 막는 쪽 <paramref name="defend"/>. 준 피해를 돌려준다.
    /// <code>
    ///   힘   = 검술 x 전투 레벨 / 50 + 검술 / 2                                     (0x00414580)
    ///   세기 = (무기 + 힘) x (4 − 지난 공격) / 4 x (25 x 공격 + (무기 있으면 50) + (무기 갈래 = 공격 + 1 이면 무기)) / 100
    ///   피해 = min(50, 세기 x ((방어 + 18) x 5 − min(막는 쪽 전투 레벨, 20) − 갑옷) / 100) + rand(치는 쪽 전투 레벨 / 2)
    ///   rand(100) ≤ (2 x 방어 + 5) x (4 − 지난 방어) x 2 − 갑옷 / 2 이면 피해 0
    /// </code>
    /// </summary>
    public int Exchange(int attack, int defend)
    {
        int a = Attacker, d = a ^ 1;
        _history[a] = (_history[a] << 2 | attack) & 0xFF;
        _history[d] = (_history[d] << 2 | defend) & 0xFF;
        var at = Sides[a];
        var df = Sides[d];
        int attPrev = (_history[a] >> 2) & 3, defPrev = (_history[d] >> 2) & 3;
        int power = at.Sword * at.BattleLevel / 50 + at.Sword / 2;
        int mult = 25 * attack + (at.Weapon != 0 ? 50 : 0) + (at.Style == attack + 1 ? at.Weapon : 0);
        int strength = (at.Weapon + power) * (4 - attPrev) / 4 * mult / 100;
        int dmg = Math.Min(50, strength * ((defend + 18) * 5 - Math.Min(df.BattleLevel, 20) - df.Armor) / 100);
        dmg = _rand(at.BattleLevel / 2) + dmg;
        if (_rand(100) <= (2 * defend + 5) * (4 - defPrev) * 2 - df.Armor / 2) dmg = 0;
        dmg = Math.Max(0, dmg);
        Rope = a == 0 ? Math.Min(Full, Rope + dmg) : Math.Max(0, Rope - Math.Min(dmg, Rope));
        Turn++;
        return dmg;
    }

    /// <summary>
    /// 남 제독의 채비(<c>0x00414E20</c>) — 무기는 전투 레벨(24 까지) / 8 로, 24 면 항해 레벨 % 3 + 3 으로 표에서,
    /// 갑옷은 항해 레벨(24 까지) / 8 로 고른다. 돌려주는 것은 아이템 번호.
    /// </summary>
    public static (int Weapon, int Armor) NpcGear(int battleLevel, int navLevel)
    {
        int[] weapons = [0x0F, 0x07, 0x0D, 0x0C, 0x0E, 0x0B];
        int[] armors = [0x11, 0x12, 0x13, 0x13];
        int b = Math.Min(battleLevel, 24), n = Math.Min(navLevel, 24);
        int w = b == 24 ? navLevel % 3 + 3 : b >> 3;
        return (weapons[w], armors[n >> 3]);
    }

    /// <summary>
    /// 일기토 뒤 주인공 경험(<c>0x004150C0</c>) — 이기면 상대 전투 레벨 x 10, 아니면 x 3. 레벨이 오르거나 상대 검술이 더 높으면
    /// 검술 · 용기가 rand(3)씩 오른다(100 까지).
    /// </summary>
    public static int ExperienceFor(bool won, int enemyBattleLevel) => enemyBattleLevel * (won ? 10 : 3);
}
