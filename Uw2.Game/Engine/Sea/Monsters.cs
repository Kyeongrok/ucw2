namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 바다 괴물 — <c>MONSTER.DAT</c> 서른 자리(칩 눈금) 가운데 열이 바다에 떠 있다(<c>0x0045A158</c> 자리 · <c>0x0045A148</c> 갈래).
/// </summary>
/// <remarks>
/// 함대가 떠 있는 괴물의 칩에 꼭 올라서면(<c>0x00447BF9</c>) 그 칸을 비우고 파일의 다음 자리를 들인 뒤(<c>0x00447D77</c>, 차례 <c>0x0049F020</c>)
/// <c>0x00447F80(갈래)</c> 가 일을 벌인다 — 손표 <c>0x00461FD0</c>, 말은 0x1FE + 2 x 갈래와 그 다음.
/// <code>
///   0 세이렌   선원 25% 를 잃는다(0x0041ACE0)        5 불새    맨 뒤(주인공 배가 아닌) 배의 돛이 반으로
///   1 큰 눈    동료마다 +0x20 이 10 까지 깎인다       6 큰 용   모든 배의 짐을 잃는다
///   2 안개     맨 뒤 배 한 척이 사라진다(선장은 쉰다)  7 용      배마다 사기 −min(10, 사기)
///   3 가오리   아무 일 없다                            8 고래    맨 뒤 배의 내구가 반으로
///   4 성 엘모  배마다 사기 += min(10, 100 − 사기)      9 유령선  주인공 운 −min(30, 운)
/// </code>
/// </remarks>
public sealed class Monsters
{
    public const int Active = 10;

    private readonly List<(int X, int Y, int Kind)> _spots;

    /// <summary>떠 있는 열 — 파일 차례(-1 은 빈 칸).</summary>
    public int[] Slots { get; private set; }

    /// <summary>다음에 들일 파일 차례.</summary>
    public int Next { get; private set; }

    public Monsters(IEnumerable<(int X, int Y, int Kind)> spots)
    {
        _spots = [.. spots];
        Slots = [.. Enumerable.Range(0, Active).Select(i => i < _spots.Count ? i : -1)];
        Next = Math.Min(Active, _spots.Count);
    }

    public (int X, int Y, int Kind) Spot(int index) => _spots[index];

    /// <summary>그 칩에 떠 있는 괴물이 있으면 갈래를 돌려주고 그 칸에 다음 자리를 들인다. 없으면 -1.</summary>
    public int Meet(int chipX, int chipY)
    {
        for (int s = 0; s < Slots.Length; s++)
        {
            int i = Slots[s];
            if (i < 0 || _spots[i].X != chipX || _spots[i].Y != chipY) continue;
            Slots[s] = _spots.Count > 0 ? Next % _spots.Count : -1;
            Next = _spots.Count > 0 ? (Next + 1) % _spots.Count : 0;
            return _spots[i].Kind;
        }
        return -1;
    }

    public void Restore(int[]? slots, int next)
    {
        if (slots == null || slots.Length != Active) return;
        Slots = [.. slots.Select(i => i >= 0 && i < _spots.Count ? i : -1)];
        Next = _spots.Count > 0 ? ((next % _spots.Count) + _spots.Count) % _spots.Count : 0;
    }
}
