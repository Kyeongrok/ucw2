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
