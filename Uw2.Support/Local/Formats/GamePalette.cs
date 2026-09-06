namespace Uw2.Support.Local.Formats;

/// <summary>
/// 열여섯 색 한 벌.
/// </summary>
/// <remarks>
/// DOS <c>MAIN.EXE</c> 가 VGA DAC 에 붓는 손(이미지 <c>0x156B</c>)을 보면 모양이 나온다.
/// <code>
///   lodsw; xchg al,ah; shl al,1; shl al,1; stosb   ; 빨강
///   lodsb;             shl al,1; shl al,1; stosb   ; 초록
///          xchg al,ah; shl al,1; shl al,1; stosb   ; 파랑
/// </code>
/// 곧 <b>색 하나가 세 바이트(파랑·빨강·초록), 성분마다 네 비트</b>이고 두 번 밀어 DAC
/// 여섯 비트로 만든다. 한 벌이 48바이트다.
///
/// 그런 벌이 <c>MAIN.EXE</c> 이미지 <c>0x3EBCE</c>·<c>0x3EBFE</c>·<c>0x3FB35</c>·<c>0x4008E</c>
/// 등에 여럿 있다. <c>0x3EBCE</c> 를 풀면 검정·파랑·빨강·자홍·초록·하늘·주황·흰색… 으로
/// 그럴듯하다. <b>어느 벌이 항해 화면인지가 아직 안 굳었다.</b>
///
/// Win95 이식판의 <c>Pal.Dat</c>(768바이트)은 6단계 216색 정육면체라 <b>게임 색이 아니다</b> —
/// 이식하며 만든 것이다.
/// </remarks>
public sealed class GamePalette
{
    /// <summary>색 수.</summary>
    public const int Count = 16;

    /// <summary>한 벌의 바이트 수.</summary>
    public const int Bytes = Count * 3;

    /// <summary>
    /// 항해 화면 벌이 든 자리(DOS <c>MAIN.EXE</c> <b>이미지</b> 기준 — 파일에서는 헤더
    /// <c>0x5400</c> 을 더한 자리다).
    /// </summary>
    public const int DosImageOffset = 0x3EBCE;

    /// <summary>
    /// 항해 화면 열여섯 색. 위 자리에서 뜬 것을 그대로 박아 뒀다.
    /// </summary>
    /// <remarks>
    /// 이 벌로 그리면 바다가 남색, 뭍이 초록, 강이 파랑, 사막이 옅은 살구, 산이 잿빛으로
    /// 나온다 — 게임 화면과 같다. 고른 방법은 이렇다.
    /// <list type="number">
    ///   <item>붓는 손(<c>0x156B</c>)에서 <b>색 하나가 세 바이트(파랑·빨강·초록)</b> 인 것을 읽는다.</item>
    ///   <item>이미지에서 48바이트가 다 열다섯 이하이고 검정으로 시작하는 자리를 다 모은다(열둘).</item>
    ///   <item>바다 칩(0)이 색인 9 를, 뭍 칩(0x41)이 13·14 를 쓰는 것을 알고 있으니
    ///         <b>9 는 파랗고 13·14 는 안 파란</b> 벌을 고른다.</item>
    /// </list>
    /// <c>0x40332</c> 에도 같은 벌이 있다. 나머지는 짙기만 다른 벌이거나 딴 화면 것이다.
    /// </remarks>
    public static readonly byte[] SeaScreenTriplets =
    [
        0x00, 0x00, 0x00,  0x0D, 0x00, 0x04,  0x00, 0x0D, 0x04,  0x0A, 0x0D, 0x06,
        0x06, 0x00, 0x0A,  0x0F, 0x00, 0x0A,  0x06, 0x0F, 0x0A,  0x0D, 0x0F, 0x0E,
        0x09, 0x07, 0x07,  0x0C, 0x00, 0x04,  0x00, 0x0A, 0x06,  0x05, 0x0E, 0x0B,
        0x0F, 0x00, 0x08,  0x06, 0x00, 0x07,  0x06, 0x00, 0x0B,  0x0C, 0x06, 0x02,
    ];

    /// <summary>항해 화면 벌.</summary>
    public static GamePalette SeaScreen() => FromTriplets(SeaScreenTriplets);

    private readonly uint[] _bgra;

    private GamePalette(uint[] bgra) => _bgra = bgra;

    /// <summary>BGRA 열여섯 칸. 셰이더 팔레트로 그대로 올린다.</summary>
    public uint[] Bgra => _bgra;

    /// <summary>48바이트 벌을 읽는다. 성분이 네 비트라 17 을 곱해 0~255 로 편다.</summary>
    public static GamePalette FromTriplets(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < Bytes) throw new InvalidDataException($"팔레트가 {Bytes}바이트보다 짧습니다");
        var bgra = new uint[Count];
        for (int i = 0; i < Count; i++)
        {
            uint b = (uint)Math.Min(255, raw[i * 3] * 17);
            uint r = (uint)Math.Min(255, raw[i * 3 + 1] * 17);
            uint g = (uint)Math.Min(255, raw[i * 3 + 2] * 17);
            bgra[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
        }
        return new GamePalette(bgra);
    }

    /// <summary>
    /// 팔레트를 못 찾았을 때 쓸 벌. 옛 EGA 열여섯 색이다 — 모양만 보려는 것이니 색은 어림이다.
    /// </summary>
    public static GamePalette Fallback()
    {
        int[] rgb =
        [
            0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
            0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF,
        ];
        var bgra = new uint[Count];
        for (int i = 0; i < Count; i++) bgra[i] = 0xFF000000u | (uint)rgb[i];
        return new GamePalette(bgra);
    }
}
