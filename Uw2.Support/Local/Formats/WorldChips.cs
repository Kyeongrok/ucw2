namespace Uw2.Support.Local.Formats;

/// <summary>
/// 세계지도 칩 벌. <c>DATA1.LZW</c> 의 <b>11번 청크 앞쪽</b>에 있다.
/// </summary>
/// <remarks>
/// 지도에 실제로 깔리는 칩은 <b>마흔한 가지</b>이고 번호가 다 <b>128 아래</b>다.
/// 그래서 앞 128장(16,384바이트)만 쓰면 된다.
///
/// 청크 크기는 판마다 다르다 — DOS 원판 32,768바이트, Win95 이식판 49,152바이트.
/// 담는 꼴도 갈리는데(<see cref="ChipSheet.Packing"/>) <b>그림은 똑같다</b>.
/// </remarks>
public static class WorldChips
{
    /// <summary><c>DATA1.LZW</c> 안에서 칩이 든 청크 번호.</summary>
    public const int ChunkIndex = 11;

    /// <summary>세계지도가 쓰는 칩 수. 그림표에 나오는 번호가 다 이 아래다.</summary>
    public const int Count = 128;

    /// <summary>앞 128장이 차지하는 바이트.</summary>
    public const int Bytes = Count * ChipSheet.BytesPerChip;   // 16384

    /// <summary>
    /// 게임 폴더에서 세계 칩 벌을 읽는다. 담는 꼴은 알아서 가린다.
    /// </summary>
    public static ChipSheet Load(string gameDirectory)
    {
        var chunks = LsArchive.ReadAll(Path.Combine(gameDirectory, "DATA1.LZW"));

        var chunk = ChunkIndex < chunks.Length && chunks[ChunkIndex].Length >= Bytes
            ? chunks[ChunkIndex]
            : chunks.FirstOrDefault(c => c.Length >= Bytes && c.Length % ChipSheet.BytesPerChip == 0)
              ?? throw new InvalidDataException("DATA1.LZW 에서 세계 칩 벌을 못 찾았습니다");

        return ChipSheet.Open(chunk.AsSpan(0, Bytes));
    }
}
