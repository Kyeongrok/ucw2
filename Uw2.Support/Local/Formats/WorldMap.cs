namespace Uw2.Support.Local.Formats;

/// <summary>
/// 세계지도. <c>WORLDMAP.LZW</c> 를 풀어 <b>1080 x 540 칸</b>으로 편다.
/// </summary>
/// <remarks>
/// 압축을 풀어도 곧장 지도가 아니다. <b>한 겹 더 있다.</b>
/// <code>
///   청크 c (c = 0,1,2)
///     0x0000  낱말 1350개   조각 i 의 자료가 시작하는 자리(리틀엔디안)
///     0x0A8C  자료부        조각 1350개
/// </code>
/// 조각 하나가 <b>12 x 12 칸</b>이고, 4,050장을 <b>90 x 45</b> 로 깐 것이 지도다.
/// 청크 셋은 <b>세로 띠</b>다 — 청크 c 가 조각 열 <c>c*30 ~ c*30+29</c>, 곧 칸 열
/// <c>c*360 ~ c*360+359</c> 를 맡는다. 게임도 한 번에 청크 하나만 메모리에 얹는다
/// (<c>0x00435850</c> 이 청크 번호를 <c>0x00456E8C</c> 에서 가져온다).
///
/// <para>조각 하나</para>
/// <code>
///   [0]      머리   0x78 ~ 0x7D · 0x80 이 켜져 있으면 통짜 조각(자국도 값도 없다)
///   [1..18]  자국   144비트, MSB 먼저. 켜져 있으면 새 값이 온다
///   [19..]   값들   자국에 켜진 비트 수만큼
/// </code>
/// <b>자국이 꺼진 칸은 "앞 값"이 아니라 <see cref="Templates">본보기 조각</see>에서
/// 가져온다.</b> 머리의 아래 일곱 비트가 본보기 여섯 벌 가운데 하나를 고른다
/// (게임 표가 <c>KOUKAI2.EXE</c> VA <c>0x004582D8</c> 에 864바이트로 박혀 있다).
/// 통짜 조각은 자국을 0 으로 놓은 것과 같아 본보기가 그대로 나온다.
///
/// 이것을 "앞 값 잇기" 로 잘못 옮겼더니 <b>칸의 18.9%가 틀렸다</b> — 본보기 넷이
/// 반반 갈린 꼴이라 해안이 통째로 어긋났다.
///
/// <para>검산</para>
/// 맞닿은 칸이 같은 값일 확률 0.906. 원판·Win95 이식판·어전편·교정판 넷이 모두 청크
/// 크기 25,394 / 21,973 / 23,907 이다. <c>MONSTER.DAT</c> 의 자리 서른 곳이 좌표를 2로
/// 나누면 다 지도 안에 든다 — 곧 <b>게임 좌표는 칸의 두 배</b>다.
///
/// 자세한 것은 볼트 <c>Project/uw2/분석/3.분석-세계지도(WORLDMAP.LZW)</c>.
/// </remarks>
public sealed class WorldMap
{
    /// <summary>지도 크기(칸). 칸 하나가 경위도 3분의 1도다.</summary>
    public const int Width = 1080, Height = 540;

    /// <summary>조각 한 변(칸).</summary>
    public const int Tile = 12;

    /// <summary>조각 격자.</summary>
    public const int TilesX = 90, TilesY = 45;

    /// <summary>청크 하나가 맡는 조각 열 수.</summary>
    public const int Band = 30;

    /// <summary>청크 하나에 든 조각 수.</summary>
    public const int TilesPerChunk = Band * TilesY;   // 1350

    /// <summary>바다 칸 값.</summary>
    public const byte Sea = 0;

    /// <summary>민뭍 칸 값. 1~14 는 해안, 16 이상은 산·숲·사막 따위다.</summary>
    public const byte Land = 15;

    /// <summary>게임 좌표에서 칸으로 가는 나눗값. 괴물 자리도 세이브도 이 눈금이다.</summary>
    public const int RawPerCell = 2;

    private readonly byte[] _cells;

    private WorldMap(byte[] cells) => _cells = cells;

    /// <summary>칸 값. 가로는 <b>둘러 감긴다</b>(경도 -180/180 잇기).</summary>
    public byte this[int x, int y]
    {
        get
        {
            int cx = ((x % Width) + Width) % Width;
            int cy = Math.Clamp(y, 0, Height - 1);
            return _cells[cy * Width + cx];
        }
    }

    /// <summary>칸 배열 원본. 셰이더에 그대로 올린다.</summary>
    public byte[] Cells => _cells;

    /// <summary>바다인지. <b>해안 칸(1~14)의 통행 여부는 아직 못 밝혔다</b> — 우선 바다로 본다.</summary>
    public bool CanSail(int x, int y) => this[x, y] != Land;

    /// <summary>구워 둔 칸 배열에서 바로 만든다(<see cref="AssetPack"/>).</summary>
    public static WorldMap FromCells(byte[] cells)
    {
        if (cells.Length != Width * Height)
            throw new ArgumentException($"칸이 {Width * Height}개가 아닙니다 ({cells.Length})");
        return new WorldMap(cells);
    }

    /// <summary>파일에서 읽는다.</summary>
    public static WorldMap Load(string path)
    {
        var chunks = LsArchive.ReadAll(path);
        if (chunks.Length != 3)
            throw new InvalidDataException($"세계지도 청크가 셋이 아닙니다 ({chunks.Length})");

        var cells = new byte[Width * Height];
        for (int c = 0; c < chunks.Length; c++) Spread(chunks[c], c, cells);
        return new WorldMap(cells);
    }

    private static void Spread(byte[] chunk, int band, byte[] cells)
    {
        int tableBytes = TilesPerChunk * 2;
        var tile = new byte[Tile * Tile];

        for (int i = 0; i < TilesPerChunk; i++)
        {
            int start = chunk[i * 2] | (chunk[i * 2 + 1] << 8);
            int end = i + 1 < TilesPerChunk
                ? chunk[(i + 1) * 2] | (chunk[(i + 1) * 2 + 1] << 8)
                : chunk.Length - tableBytes;

            Decode(chunk.AsSpan(tableBytes + start, end - start), tile);

            int ty = i / Band, tx = band * Band + i % Band;
            for (int y = 0; y < Tile; y++)
            {
                int dst = (ty * Tile + y) * Width + tx * Tile;
                tile.AsSpan(y * Tile, Tile).CopyTo(cells.AsSpan(dst, Tile));
            }
        }
    }

    private static void Decode(ReadOnlySpan<byte> row, byte[] tile)
    {
        byte head = row[0];
        var template = Templates[(head & 0x7F) - FirstHead];

        if ((head & 0x80) != 0)                       // 통짜 조각 — 자국이 다 꺼진 것과 같다
        {
            template.CopyTo(tile, 0);
            return;
        }

        var mark = row.Slice(1, 18);
        var vals = row[19..];
        int vi = 0;

        for (int b = 0; b < Tile * Tile; b++)
            tile[b] = ((mark[b >> 3] >> (7 - (b & 7))) & 1) != 0 ? vals[vi++] : template[b];
    }

    /// <summary>머리 값이 시작하는 자리. <c>0x78</c> ~ <c>0x7D</c> 여섯이다.</summary>
    public const int FirstHead = 0x78;

    /// <summary>
    /// 본보기 조각 여섯 벌. 게임은 <c>KOUKAI2.EXE</c> VA <c>0x004582D8</c> 에 864바이트로
    /// 들고 있는데, 넷이 반반 갈린 꼴이고 둘이 통짜라 여기서는 만들어 쓴다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x78  왼쪽 여섯 줄 뭍  · 오른쪽 바다
    ///   0x79  왼쪽 바다        · 오른쪽 여섯 줄 뭍
    ///   0x7A  위 여섯 줄 뭍    · 아래 바다
    ///   0x7B  위 바다          · 아래 여섯 줄 뭍
    ///   0x7C  온통 뭍
    ///   0x7D  온통 바다
    /// </code>
    /// </remarks>
    public static readonly byte[][] Templates = BuildTemplates();

    private static byte[][] BuildTemplates()
    {
        var t = new byte[6][];
        for (int k = 0; k < t.Length; k++)
        {
            var c = new byte[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    c[y * Tile + x] = k switch
                    {
                        0 => x < Tile / 2 ? Land : Sea,
                        1 => x < Tile / 2 ? Sea : Land,
                        2 => y < Tile / 2 ? Land : Sea,
                        3 => y < Tile / 2 ? Sea : Land,
                        4 => Land,
                        _ => Sea,
                    };
            t[k] = c;
        }
        return t;
    }
}
