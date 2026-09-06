namespace Uw2.Support.Local.Formats;

/// <summary>
/// <c>MONSTER.DAT</c> — 바다괴물이 나오는 자리. 5바이트 레코드가 <b>서른 개</b>고 뒤는 0 이다
/// (칸은 마흔).
/// </summary>
/// <remarks>
/// <code>
///   uint16 x;     // 488 ~ 1971
///   uint16 y;     //  21 ~ 1067
///   uint8  kind;  // 0~9 가운데 아홉 (7번은 안 쓴다)
/// </code>
/// 좌표는 리틀엔디안이고 <b>칸의 두 배</b>다 — 2로 나누면 서른 곳이 다 1080 x 540 안에 든다.
/// 이것이 세계지도 크기를 굳힌 증거의 하나다.
/// </remarks>
public sealed class MonsterTable
{
    /// <summary>레코드 크기.</summary>
    public const int RecordSize = 5;

    /// <summary>표의 칸 수(파일은 200바이트).</summary>
    public const int Slots = 40;

    /// <summary>괴물 자리 하나. <paramref name="X"/>·<paramref name="Y"/> 는 게임 좌표(칸의 두 배).</summary>
    public readonly record struct Spot(int X, int Y, int Kind)
    {
        /// <summary>지도 칸 자리.</summary>
        public (int X, int Y) Cell => (X / WorldMap.RawPerCell, Y / WorldMap.RawPerCell);
    }

    /// <summary>쓰이는 자리들.</summary>
    public IReadOnlyList<Spot> Spots { get; }

    private MonsterTable(List<Spot> spots) => Spots = spots;

    /// <summary>파일에서 읽는다. 좌표가 둘 다 0 인 칸에서 멈춘다.</summary>
    public static MonsterTable Load(string path)
    {
        var d = File.ReadAllBytes(path);
        var spots = new List<Spot>();
        for (int i = 0; i + RecordSize <= d.Length && i / RecordSize < Slots; i += RecordSize)
        {
            int x = d[i] | (d[i + 1] << 8);
            int y = d[i + 2] | (d[i + 3] << 8);
            if (x == 0 && y == 0) break;
            spots.Add(new Spot(x, y, d[i + 4]));
        }
        return new MonsterTable(spots);
    }
}
