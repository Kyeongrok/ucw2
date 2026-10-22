namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 바다 만남 — 남의 함대가 덮칠 때의 목록 54(교전 · 도망 · 항복, <c>0x00410EC4</c>).
/// 머리 말은 0x333(원수의 함대여) 뒤, 제 힘의 3/2 가 남보다 작으면 0x334(위험), 아니면 0x335(반격).
/// </summary>
/// <remarks>
/// 여기는 항복 셈만 있다 — 만남을 일으키는 셈은 <see cref="Sighting"/> · <see cref="NpcFleet"/>, 교전은 <see cref="Battle"/> 이다.
/// </remarks>
public static class Encounter
{
    /// <summary>
    /// 해전 뒤 적 제독의 마지막 말(<c>0x0040AC16</c>) — 결과가 적 기함 이탈(2) · 적 기함 침몰(4) · 일기토 승(6)일 때만.
    /// 해적(함대 번호 / 10 이 6)은 0x342, 상선(번호 % 10 이 1 ~ 4)은 0x344, 그 밖(해군)은 0x343 이고, 이탈이 아니면(죽거나 붙잡혔으면) 4 를 뺀다
    /// (0x33E 「이까짓 놈에게」 · 0x340 「살려 줘」 · 0x33F 「조국이 언젠가」). 없으면 -1.
    /// </summary>
    public static int PartingLine(int result, int fleetIndex)
    {
        if (result is not (Battle.Outcome.EnemyLeft or Battle.Outcome.EnemySunk or Battle.Outcome.DuelWon)) return -1;
        int line = fleetIndex / 10 == 6 ? 0x342 : fleetIndex % 10 is >= 1 and <= 4 ? 0x344 : 0x343;
        return result == Battle.Outcome.EnemyLeft ? line : line - 4;
    }

    /// <summary>
    /// 항복(<c>0x00410F3A</c>) — 나라 함대면 말 0x339, 소지금과 짐마다 양이 2/3 로(0 이 된 칸은 빈다).
    /// 해적(나라 6)이면 말 0x33A 뒤 짐을 다 잃고, 소지금이 1000 아래면 +1000(0x33B), 10000 아래면 그대로(0x33C), 그 위면 5000(0x33D).
    /// 돌려주는 것은 뒤에 띄울 말(나라 함대는 0x339 하나라 -1).
    /// </summary>
    public static int Surrender(PlayerState player, bool pirate)
    {
        int keep = pirate ? 0 : 2;
        foreach (var ship in player.Ships)
            for (int k = 0; k < ship.Cargo.Length; k++)
            {
                int left = ship.Cargo[k].Amount * keep / 3;
                ship.Cargo[k] = left == 0 ? (-1, 0) : (ship.Cargo[k].Good, left);
            }
        if (!pirate)
        {
            player.SetGold(player.Gold * 2 / 3);
            return -1;
        }
        if (player.Gold < 1000) { player.SetGold(player.Gold + 1000); return 0x33B; }
        if (player.Gold < 10000) return 0x33C;
        player.SetGold(5000);
        return 0x33D;
    }
}
