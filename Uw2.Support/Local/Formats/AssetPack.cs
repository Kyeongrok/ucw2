using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 게임 폴더에서 뽑아 구워 둔 자산 한 벌. <b>게임 없이도 돌아가게</b> 하는 것이 목적이다.
/// </summary>
/// <remarks>
/// 3편 <c>cds-helper</c> 의 <c>asset/</c> 과 같은 자리다. 다른 것은 <b>거의 다 색인 PNG</b>로
/// 굽는다는 점이다 — 값이 그대로 남으면서 GitHub 에서 그림으로도 뜬다
/// (<see cref="IndexedPng"/>).
///
/// <code>
///   asset/
///     palette.png        16x1     열여섯 색 그 자체
///     font.png           128x96   8x16 글자 아흔여섯 자 (HANKAKU.FNT)
///     ship.png           64x80    부두에 매인 배를 오려 낸 것(255 는 비침)
///     char.png           128x128  도시에서 걷는 사람 16x32 서른두 장(15 는 비침)
///     ports.json                  항구 이름·자리·칩 벌
///     monsters.json               괴물 자리 서른 곳
///     wind.png           30x45    표 셋을 세로로 쌓음(값 = 날바이트)
///     world/
///       cells.png        1080x540 값 = 칸 값
///       chips.png        256x128  값 = 색인 0~15  (칩 128장)
///       quad.png         256x4    값 = 칩 번호    (그림표)
///     port/
///       maps.png         1056x960 96x96 지도 101장을 11x10 으로 깔았다(값 = 칩 번호)
///       chips-0..6.png   256x240  값 = 색인 0~15  (벌마다 칩 240장)
///       chipno.png       100x1    값 = 벌 번호
/// </code>
/// </remarks>
public sealed class AssetPack
{
    /// <summary>자산 폴더 이름.</summary>
    public const string FolderName = "asset";

    /// <summary>항구지도를 몇 개씩 늘어놓는지.</summary>
    public const int PortMapCols = 11;

    /// <summary>칩 아틀라스 한 줄에 놓는 칩 수.</summary>
    public const int AtlasCols = 16;

    // ------------------------------------------------------------------ 굽기

    /// <summary>
    /// 게임 폴더에서 뽑아 <paramref name="outDir"/> 에 굽는다.
    /// </summary>
    /// <param name="alsoLookIn">
    /// 없는 파일을 더 찾아 볼 폴더. Win95 이식판에는 <c>HANKAKU.FNT</c> 가 없어서
    /// DOS 원판 폴더를 여기에 준다.
    /// </param>
    public static void Bake(string gameDirectory, string outDir, string? alsoLookIn = null)
    {
        string Find(string name)
        {
            string a = Path.Combine(gameDirectory, name);
            if (File.Exists(a)) return a;
            if (alsoLookIn != null)
            {
                string b = Path.Combine(alsoLookIn, name);
                if (File.Exists(b)) return b;
            }
            return a;                                  // 없으면 부르는 쪽이 터진다
        }

        PortTable.RegisterEncodings();
        Directory.CreateDirectory(Path.Combine(outDir, "world"));
        Directory.CreateDirectory(Path.Combine(outDir, "port"));

        var pal = GamePalette.SeaScreenRgb;

        // 팔레트 그 자체 — 열여섯 칸을 옆으로 늘어놓는다.
        IndexedPng.Write(Path.Combine(outDir, "palette.png"),
                         [.. Enumerable.Range(0, GamePalette.Count).Select(i => (byte)i)],
                         GamePalette.Count, 1, pal);

        // 세계지도
        var map = WorldMap.Load(Find("WORLDMAP.LZW"));
        IndexedPng.Write(Path.Combine(outDir, "world", "cells.png"),
                         map.Cells, WorldMap.Width, WorldMap.Height, CellPalette());

        var sheet = WorldChips.Load(gameDirectory);
        var atlas = sheet.ToAtlas(AtlasCols, out int cw, out int ch);
        IndexedPng.Write(Path.Combine(outDir, "world", "chips.png"), atlas, cw, ch, pal);

        var quads = QuadBytes(gameDirectory);
        IndexedPng.Write(Path.Combine(outDir, "world", "quad.png"), quads, ChipMap.QuadCount, 4,
                         DataPalette());

        // 항구
        var maps = PortMap.Load(Find("PORTMAP.LZW"));
        var (tiled, tw, th) = TilePortMaps(maps);
        IndexedPng.Write(Path.Combine(outDir, "port", "maps.png"), tiled, tw, th, DataPalette());

        var sets = PortChipSets.Load(Find("PORTCHIP.LZW"));
        for (int s = 0; s < PortChipSets.SetCount; s++)
        {
            var a = sets[s].ToAtlas(AtlasCols, out int aw, out int ah);
            IndexedPng.Write(Path.Combine(outDir, "port", $"chips-{s}.png"), a, aw, ah, pal);
        }

        var chipNo = File.ReadAllBytes(Find("CHIP_NO.DAT"));
        IndexedPng.Write(Path.Combine(outDir, "port", "chipno.png"),
                         chipNo, chipNo.Length, 1, DataPalette());

        // 바람·해류 — 표 셋을 세로로 쌓는다.
        var wind = File.ReadAllBytes(Find("WINDCUR.DAT"));
        IndexedPng.Write(Path.Combine(outDir, "wind.png"), wind,
                         WindCurTable.Cols, WindCurTable.Rows * WindCurTable.Pages, DataPalette());

        // 표는 사람이 읽게 JSON 으로
        var table = PortTable.Load(gameDirectory);
        if (table != null)
            WriteJson(Path.Combine(outDir, "ports.json"),
                      table.Ports.Select(p => new PortJson(p.Index, p.Name, p.X, p.Y, p.Nation)).ToArray());

        var monsters = MonsterTable.Load(Find("MONSTER.DAT"));
        WriteJson(Path.Combine(outDir, "monsters.json"),
                  monsters.Spots.Select(s => new MonsterJson(s.X, s.Y, s.Kind)).ToArray());

        // 배 그림 — 리스본 부두에 매인 배를 오려 낸다.
        BakeShip(maps, sets, Path.Combine(outDir, "ship.png"));

        // 사람 그림 — 도시에서 걷는 그것이다. CHAR.LZW 청크 하나가 사람 하나다.
        string charPath = Find("CHAR.LZW");
        if (File.Exists(charPath))
        {
            var first = LsArchive.ReadAll(charPath)[0];
            var walk = WalkerSheet(first, out int ww, out int wh);
            IndexedPng.Write(Path.Combine(outDir, "char.png"), walk, ww, wh, pal);
        }

        // 글꼴 — 첫 메뉴에 쓰는 그것이다. Win95 이식판에는 없어 원판에서 가져온다.
        string fnt = Find("HANKAKU.FNT");
        if (File.Exists(fnt))
        {
            var glyphs = GameFont.Load(fnt).ToSheet(out int fw, out int fh);
            IndexedPng.Write(Path.Combine(outDir, "font.png"), glyphs, fw, fh, InkPalette());
        }
    }

    /// <summary>배 그림이 놓인 자리 — 리스본(0번) 항구지도의 칸이다.</summary>
    public const int ShipPort = 0, ShipCellX = 1, ShipCellY = 74, ShipCellW = 4, ShipCellH = 5;

    /// <summary>배 그림에서 <b>비침</b>을 뜻하는 색인. 게임 팔레트가 0~15 라 255 는 비어 있다.</summary>
    public const byte Transparent = 255;

    /// <summary>배 그림 크기(점).</summary>
    public const int ShipW = ShipCellW * ChipSheet.Size, ShipH = ShipCellH * ChipSheet.Size;

    /// <summary>
    /// 리스본 부두에 매인 배를 오려 낸다.
    /// </summary>
    /// <remarks>
    /// 게임에는 세계지도용 배 그림이 따로 없다 — 부두에 놓인 것이 곧 배 그림이다.
    /// 바닷물을 걷어 내는 방법이 재미있다. 물 칩은 자리에 따라 무늬가 도드라지므로,
    /// <b>같은 자리의 물 칩 점과 같으면 비침</b>으로 친다. 돛과 밧줄만 남는다.
    /// </remarks>
    private static void BakeShip(PortMap maps, PortChipSets sets, string path)
    {
        var sheet = sets[0];
        var cells = maps[ShipPort];
        int water = cells[(ShipCellY + ShipCellH - 1) * PortMap.Size];   // 배 왼쪽은 늘 물이다

        var px = new byte[ShipW * ShipH];
        for (int cy = 0; cy < ShipCellH; cy++)
            for (int cx = 0; cx < ShipCellW; cx++)
            {
                int t = cells[(ShipCellY + cy) * PortMap.Size + ShipCellX + cx];
                for (int y = 0; y < ChipSheet.Size; y++)
                    for (int x = 0; x < ChipSheet.Size; x++)
                    {
                        byte v = sheet[t, x, y];
                        if (t == water || v == sheet[water, x, y]) v = Transparent;
                        px[(cy * ChipSheet.Size + y) * ShipW + cx * ChipSheet.Size + x] = v;
                    }
            }
        IndexedPng.Write(path, px, ShipW, ShipH, GamePalette.SeaScreenRgb);
    }

    /// <summary>사람 그림 한 장 크기와 한 줄에 놓는 수.</summary>
    public const int CharW = 32, CharH = 32, CharCols = CharPoses;

    /// <summary>사람 하나에 든 그림 수 — 네 쪽에 걸음이 둘씩이다.</summary>
    public const int CharPoses = 8;

    /// <summary>
    /// 사람 그림에서 비침으로 쓰는 색인. 그림의 바탕이 <b>검정(0)</b>이다.
    /// </summary>
    /// <remarks>
    /// 딸린 가림장이 이르는 것과 <b>똑같다</b> — 가림장이 「밖」(15)이라 이른 자리는 그림도
    /// 어김없이 0 이었다(첫 장에서 154 점, 어긋난 것 없음). 그래서 가림장은 굽지 않는다.
    /// </remarks>
    public const byte CharClear = 0;

    /// <summary>
    /// <c>CHAR.LZW</c> 청크 하나를 32x32 그림 여덟 장으로 펴서 한 줄에 늘어놓는다.
    /// </summary>
    /// <remarks>
    /// 청크 8192 바이트는 <b>16x16 4비트 조각 예순넷</b>인데, 얼개가 이렇다.
    /// <code>
    ///   조각 0 그림 왼위   조각 1 가림 왼위
    ///   조각 2 그림 오위   조각 3 가림 오위
    ///   조각 4 그림 왼아래 조각 5 가림 왼아래
    ///   조각 6 그림 오아래 조각 7 가림 오아래   → 여기까지가 그림 한 장(32x32)
    /// </code>
    /// 그러니 여덟 조각이 한 장이고, 청크 하나가 <b>사람 하나에 여덟 장</b>이다.
    /// 쪽은 <see cref="CharPose"/> 가 가른다.
    ///
    /// 조각 폭 16 은 자료를 세로로 견주어 골랐고(줄 너비 여덟 바이트에서 어긋남이 가장 적다),
    /// 조각 넷을 붙여야 사람이 온전해지는 것은 <b>그려 보고</b> 굳혔다 — 조각 하나만 보면
    /// 사람이 왼쪽·오른쪽으로 잘려 있다.
    /// </remarks>
    private static byte[] WalkerSheet(byte[] chunk, out int width, out int height)
    {
        const int Q = 16, QBytes = Q * Q / 2;          // 조각 하나
        int n = Math.Min(chunk.Length / (QBytes * 8), CharPoses);

        width = CharCols * CharW;
        height = CharH;

        var px = new byte[width * height];
        for (int t = 0; t < n; t++)
        {
            int ox = t * CharW;

            // 조각 넷을 왼위·오위·왼아래·오아래 자리에 붙인다. 사이에 낀 가림장은 건너뛴다.
            for (int q = 0; q < 4; q++)
            {
                int bx = (q & 1) * Q, by = (q >> 1) * Q;
                int at = (t * 8 + q * 2) * QBytes;

                for (int y = 0; y < Q; y++)
                    for (int x = 0; x < Q; x++)
                    {
                        byte b = chunk[at + y * (Q / 2) + (x >> 1)];
                        px[(by + y) * width + ox + bx + x] =
                            (byte)((x & 1) == 0 ? (b >> 4) & 15 : b & 15);
                    }
            }
        }
        return px;
    }

    /// <summary>
    /// 보는 쪽과 걸음에서 그림 번호를 낸다. 자료에 든 차례는
    /// <c>0 위 · 2 왼쪽 · 4 아래 · 6 오른쪽</c> 이다.
    /// </summary>
    /// <remarks>
    /// 가로 짝은 자료로 굳혔다 — 2↔6 과 3↔7 이 점 하나 안 틀리고 서로 뒤집힌 꼴이다.
    /// 위·아래는 <b>화면에 띄워 놓고</b> 갈랐다. 4 는 눈이 보이는 앞모습이고 0 은 뒤통수라,
    /// 처음에 0 을 앞으로 잡았던 것을 뒤집었다.
    /// </remarks>
    /// <param name="facing">0 아래 · 1 왼쪽 · 2 오른쪽 · 3 위 — <c>Walker.Facing</c> 차례다.</param>
    /// <param name="secondStep">걸음 두 장 가운데 뒤엣것이면 참.</param>
    public static int CharPose(int facing, bool secondStep)
    {
        int lane = facing switch { 1 => 1, 2 => 3, 3 => 0, _ => 2 };
        return lane * 2 + (secondStep ? 1 : 0);
    }

    /// <summary>한 비트 그림에 다는 팔레트 — 0 은 검정, 1 은 흰빛.</summary>
    private static byte[] InkPalette()
    {
        var p = new byte[256 * 3];
        for (int v = 1; v < 256; v++) { p[v * 3] = 0xF0; p[v * 3 + 1] = 0xF0; p[v * 3 + 2] = 0xF0; }
        return p;
    }

    // ------------------------------------------------------------------ 읽기

    /// <summary>구워 둔 자산을 읽는다. 한 조각이라도 없으면 null.</summary>
    public static AssetPack? Load(string dir)
    {
        try
        {
            var cells = IndexedPng.Read(Path.Combine(dir, "world", "cells.png"));
            if (cells.Width != WorldMap.Width || cells.Height != WorldMap.Height) return null;

            var chips = IndexedPng.Read(Path.Combine(dir, "world", "chips.png"));
            var quad = IndexedPng.Read(Path.Combine(dir, "world", "quad.png"));
            var maps = IndexedPng.Read(Path.Combine(dir, "port", "maps.png"));
            var chipNo = IndexedPng.Read(Path.Combine(dir, "port", "chipno.png"));
            var wind = IndexedPng.Read(Path.Combine(dir, "wind.png"));

            var sets = new byte[PortChipSets.SetCount][];
            for (int s = 0; s < sets.Length; s++)
                sets[s] = IndexedPng.Read(Path.Combine(dir, "port", $"chips-{s}.png")).Pixels;

            var ports = ReadJson<PortJson[]>(Path.Combine(dir, "ports.json")) ?? [];

            string charPath2 = Path.Combine(dir, "char.png");
            byte[] walker = []; int walkerW = 0, walkerH = 0;
            if (File.Exists(charPath2))
            {
                var w = IndexedPng.Read(charPath2);
                walker = w.Pixels; walkerW = w.Width; walkerH = w.Height;
            }

            string shipPath = Path.Combine(dir, "ship.png");
            byte[] ship = File.Exists(shipPath) ? IndexedPng.Read(shipPath).Pixels : [];

            string fontPath = Path.Combine(dir, "font.png");
            GameFont? font = null;
            if (File.Exists(fontPath))
            {
                var f = IndexedPng.Read(fontPath);
                font = GameFont.FromSheet(f.Pixels, f.Width);
            }

            return new AssetPack
            {
                Cells = cells.Pixels,
                WorldAtlas = chips.Pixels,
                WorldAtlasSize = (chips.Width, chips.Height),
                Quads = quad.Pixels,
                PortMaps = UntilePortMaps(maps),
                PortAtlases = sets,
                PortAtlasSize = (AtlasCols * ChipSheet.Size,
                                 PortChipSets.ChipsPerSet / AtlasCols * ChipSheet.Size),
                PortChipSet = chipNo.Pixels,
                Wind = wind.Pixels,
                Ports = [.. ports.Select(p => new PortTable.Port(p.Index, p.Name, p.X, p.Y, p.Nation))],
                Ship = ship,
                Walker = walker,
                WalkerSize = (walkerW, walkerH),
                Font = font,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>실행 파일 옆의 <c>asset/</c> 을 찾는다. 없으면 null.</summary>
    public static string? FindFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 6 && dir != null; up++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, FolderName);
            if (File.Exists(Path.Combine(candidate, "world", "cells.png"))) return candidate;
        }
        return null;
    }

    /// <summary>세계지도 칸 값(1080 x 540).</summary>
    public byte[] Cells { get; private init; } = [];

    /// <summary>세계 칩 아틀라스(색인 0~15)와 그 크기.</summary>
    public byte[] WorldAtlas { get; private init; } = [];
    public (int W, int H) WorldAtlasSize { get; private init; }

    /// <summary>그림표 256 x 4.</summary>
    public byte[] Quads { get; private init; } = [];

    /// <summary>항구지도 101장(각 96 x 96).</summary>
    public byte[][] PortMaps { get; private init; } = [];

    /// <summary>항구 칩 벌 일곱의 아틀라스와 그 크기.</summary>
    public byte[][] PortAtlases { get; private init; } = [];
    public (int W, int H) PortAtlasSize { get; private init; }

    /// <summary>항구마다 쓰는 칩 벌.</summary>
    public byte[] PortChipSet { get; private init; } = [];

    /// <summary>바람·해류 날바이트 1,350.</summary>
    public byte[] Wind { get; private init; } = [];

    /// <summary>항구 이름·자리.</summary>
    public IReadOnlyList<PortTable.Port> Ports { get; private init; } = [];

    /// <summary>게임 글꼴. 없으면 null.</summary>
    public GameFont? Font { get; private init; }

    /// <summary>배 그림(64 x 80 색인). <see cref="Transparent"/> 는 비침이다.</summary>
    public byte[] Ship { get; private init; } = [];

    /// <summary>사람 그림 판(16x32 장을 여덟씩 늘어놓은 것)과 그 크기.</summary>
    public byte[] Walker { get; private init; } = [];
    public (int W, int H) WalkerSize { get; private init; }

    // ------------------------------------------------------------------ 잔손

    private static byte[] QuadBytes(string gameDirectory)
    {
        foreach (var c in LsArchive.ReadAll(Path.Combine(gameDirectory, "DATA1.LZW")))
            if (c.Length == ChipMap.QuadCount * 4) return Transpose(c);
        throw new InvalidDataException("DATA1.LZW 에 1,024바이트 그림표가 없습니다");
    }

    /// <summary>그림표를 256폭 x 4줄로 눕힌다 — 줄마다 네 귀 가운데 하나가 된다.</summary>
    private static byte[] Transpose(byte[] quad)
    {
        var px = new byte[ChipMap.QuadCount * 4];
        for (int v = 0; v < ChipMap.QuadCount; v++)
            for (int k = 0; k < 4; k++)
                px[k * ChipMap.QuadCount + v] = quad[v * 4 + k];
        return px;
    }

    private static byte[] Untranspose(byte[] px)
    {
        var quad = new byte[ChipMap.QuadCount * 4];
        for (int v = 0; v < ChipMap.QuadCount; v++)
            for (int k = 0; k < 4; k++)
                quad[v * 4 + k] = px[k * ChipMap.QuadCount + v];
        return quad;
    }

    /// <summary>그림표를 <see cref="ChipMap"/> 이 쓰는 꼴로.</summary>
    public ChipMap ToChipMap() => ChipMap.FromQuadTable(Untranspose(Quads));

    private static (byte[] Pixels, int W, int H) TilePortMaps(PortMap maps)
    {
        int rows = (maps.Ports + PortMapCols - 1) / PortMapCols;
        int w = PortMapCols * PortMap.Size, h = rows * PortMap.Size;
        var px = new byte[w * h];
        for (int p = 0; p < maps.Ports; p++)
        {
            int ox = (p % PortMapCols) * PortMap.Size, oy = (p / PortMapCols) * PortMap.Size;
            for (int y = 0; y < PortMap.Size; y++)
                maps[p].AsSpan(y * PortMap.Size, PortMap.Size).CopyTo(px.AsSpan((oy + y) * w + ox));
        }
        return (px, w, h);
    }

    private static byte[][] UntilePortMaps(IndexedPng.Image img)
    {
        int rows = img.Height / PortMap.Size;
        var list = new List<byte[]>();
        for (int p = 0; p < rows * PortMapCols && list.Count < PortMap.Count; p++)
        {
            int ox = (p % PortMapCols) * PortMap.Size, oy = (p / PortMapCols) * PortMap.Size;
            var one = new byte[PortMap.Size * PortMap.Size];
            for (int y = 0; y < PortMap.Size; y++)
                img.Pixels.AsSpan((oy + y) * img.Width + ox, PortMap.Size)
                          .CopyTo(one.AsSpan(y * PortMap.Size));
            list.Add(one);
        }
        return [.. list];
    }

    /// <summary>칸 값을 눈으로 보라고 만든 팔레트 — 바다 남색, 뭍 미색, 그 밖 붉은 기.</summary>
    private static byte[] CellPalette()
    {
        var p = new byte[256 * 3];
        for (int v = 0; v < 256; v++)
        {
            (byte r, byte g, byte b) = v == WorldMap.Sea ? ((byte)0x17, (byte)0x37, (byte)0x6F)
                                     : v == WorldMap.Land ? ((byte)0xE2, (byte)0xD6, (byte)0xB0)
                                     : ((byte)0xB0, (byte)0x5C, (byte)0x40);
            p[v * 3] = r; p[v * 3 + 1] = g; p[v * 3 + 2] = b;
        }
        return p;
    }

    /// <summary>값이 색이 아닌 그림에 다는 팔레트. 그저 눈에 갈리라고 둔 것이다.</summary>
    private static byte[] DataPalette()
    {
        var p = new byte[256 * 3];
        for (int v = 0; v < 256; v++)
        {
            p[v * 3] = (byte)(v * 7 % 256);
            p[v * 3 + 1] = (byte)(v * 13 % 256);
            p[v * 3 + 2] = (byte)(v * 29 % 256);
        }
        return p;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : default;

    private record PortJson(
        [property: JsonPropertyName("번호")] int Index,
        [property: JsonPropertyName("이름")] string Name,
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("나라")] byte Nation);

    private record MonsterJson(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("종류")] int Kind);
}
