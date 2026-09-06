namespace Uw2.Support.Local.Formats;

/// <summary>
/// 16 x 16 칩 한 벌. 색인 0~15 를 낸다.
/// </summary>
/// <remarks>
/// 한 장이 <b>128바이트</b>인 것은 두 판이 같은데 <b>점을 담는 꼴이 다르다</b>.
///
/// <para>DOS 원판 — 면 넷 겹판</para>
/// <code>
///   면 p (p=0..3) = t*128 + p*32
///   줄 y          = ... + y*2        (한 줄 두 바이트 = 열여섯 점)
///   점 x 의 비트  = MSB 먼저
///   색인 = 면0비트 | 면1비트&lt;&lt;1 | 면2비트&lt;&lt;2 | 면3비트&lt;&lt;3
/// </code>
///
/// <para>Win95 이식판 — 4비트 꽉 채움</para>
/// <code>
///   줄 y  = t*128 + y*8              (한 줄 여덟 바이트 = 열여섯 점)
///   짝수 점은 윗 니블, 홀수 점은 아랫 니블
/// </code>
///
/// <b>그림은 두 판이 똑같다.</b> 이식하며 담는 꼴만 바꾼 것이라, 어느 쪽으로 풀든
/// 칩 0 은 색인 9 가 181점 · 0 이 49점으로 나온다. 그래서 <see cref="Open"/> 이
/// <b>둘 다 풀어 보고 그럴듯한 쪽</b>을 고른다.
/// </remarks>
public sealed class ChipSheet
{
    /// <summary>칩 한 변(점).</summary>
    public const int Size = 16;

    /// <summary>칩 하나가 차지하는 바이트. 두 꼴이 같다.</summary>
    public const int BytesPerChip = Size * Size / 2;   // 128

    /// <summary>점을 담는 꼴.</summary>
    public enum Packing
    {
        /// <summary>DOS 원판 — 면 넷 겹판.</summary>
        Planar,
        /// <summary>Win95 이식판 — 4비트 꽉 채움.</summary>
        Packed4,
    }

    private readonly byte[] _indices;   // [칩][점] 색인 0~15

    /// <summary>칩 수.</summary>
    public int Count { get; }

    /// <summary>어느 꼴로 풀었는지.</summary>
    public Packing Kind { get; }

    private ChipSheet(byte[] indices, int count, Packing kind)
    {
        _indices = indices; Count = count; Kind = kind;
    }

    /// <summary>칩 <paramref name="chip"/> 의 점 (<paramref name="x"/>, <paramref name="y"/>) 색인.</summary>
    public byte this[int chip, int x, int y] => _indices[(chip * Size + y) * Size + x];

    /// <summary>색인 배열 원본.</summary>
    public byte[] Indices => _indices;

    /// <summary>
    /// 담는 꼴을 <b>알아서 가려</b> 푼다.
    /// </summary>
    /// <remarks>
    /// 잘못 풀면 한 칩에 색인이 열대여섯 가지로 흩어지고, 제대로 풀면 서너 가지로 뭉친다.
    /// 앞쪽 칩 몇 장의 <b>색인 가짓수 평균</b>이 적은 쪽을 고른다.
    /// </remarks>
    public static ChipSheet Open(ReadOnlySpan<byte> data)
    {
        var planar = FromPlanar(data);
        var packed = FromPacked4(data);
        return Spread(planar) <= Spread(packed) ? planar : packed;
    }

    /// <summary>앞쪽 칩 여덟 장의 색인 가짓수 평균. 작을수록 제대로 푼 것이다.</summary>
    private static double Spread(ChipSheet s)
    {
        int probe = Math.Min(8, s.Count);
        if (probe == 0) return double.MaxValue;

        int total = 0;
        for (int t = 0; t < probe; t++)
        {
            var seen = 0;                       // 색인 0~15 를 비트로 센다
            for (int i = 0; i < Size * Size; i++) seen |= 1 << s._indices[t * Size * Size + i];
            total += System.Numerics.BitOperations.PopCount((uint)seen);
        }
        return total / (double)probe;
    }

    /// <summary>면 넷 겹판(DOS 원판)으로 푼다.</summary>
    public static ChipSheet FromPlanar(ReadOnlySpan<byte> data)
    {
        int count = data.Length / BytesPerChip;
        var px = new byte[count * Size * Size];

        for (int t = 0; t < count; t++)
        {
            int chip = t * BytesPerChip;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int v = 0;
                    for (int p = 0; p < 4; p++)
                    {
                        byte b = data[chip + p * 32 + y * 2 + (x >> 3)];
                        v |= ((b >> (7 - (x & 7))) & 1) << p;
                    }
                    px[(t * Size + y) * Size + x] = (byte)v;
                }
        }
        return new ChipSheet(px, count, Packing.Planar);
    }

    /// <summary>4비트 꽉 채움(Win95 이식판)으로 푼다.</summary>
    public static ChipSheet FromPacked4(ReadOnlySpan<byte> data)
    {
        int count = data.Length / BytesPerChip;
        var px = new byte[count * Size * Size];

        for (int t = 0; t < count; t++)
        {
            int chip = t * BytesPerChip;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    byte b = data[chip + y * (Size / 2) + (x >> 1)];
                    px[(t * Size + y) * Size + x] = (byte)((x & 1) == 0 ? (b >> 4) & 15 : b & 15);
                }
        }
        return new ChipSheet(px, count, Packing.Packed4);
    }

    /// <summary>구워 둔 아틀라스를 도로 칩 벌로. <paramref name="cols"/> 개씩 놓인 것이다.</summary>
    public static ChipSheet FromAtlas(byte[] atlas, int width, int height, int cols, Packing kind)
    {
        int rows = height / Size;
        int count = rows * cols;
        var px = new byte[count * Size * Size];
        for (int t = 0; t < count; t++)
        {
            int ox = (t % cols) * Size, oy = (t / cols) * Size;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    px[(t * Size + y) * Size + x] = atlas[(oy + y) * width + ox + x];
        }
        return new ChipSheet(px, count, kind);
    }

    /// <summary>
    /// 셰이더에 올릴 아틀라스로 편다 — 칩을 <paramref name="cols"/> 개씩 격자로 늘어놓는다.
    /// </summary>
    public byte[] ToAtlas(int cols, out int width, out int height)
    {
        int rows = Math.Max(1, (Count + cols - 1) / cols);
        width = cols * Size;
        height = rows * Size;
        var atlas = new byte[width * height];

        for (int t = 0; t < Count; t++)
        {
            int ox = (t % cols) * Size, oy = (t / cols) * Size;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    atlas[(oy + y) * width + ox + x] = this[t, x, y];
        }
        return atlas;
    }
}
