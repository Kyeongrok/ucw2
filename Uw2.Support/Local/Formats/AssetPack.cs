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
///     ships.json                  선종 스물다섯 (SDATA.DAT)
///     people.json                 사람 백스물 — 이름 · 능력치 · 레벨 (SDATA.DAT)
///     fleets.png         133x70   새 게임 함대 일흔(값 = 날바이트, SDATA.DAT 0x247B)
///     people-raw.png     50x120   사람 백스물 날바이트(SDATA.DAT 0xCAF) — 시나리오 기억
///     nations.png        32x7     나라 일곱 날바이트(SDATA.DAT 0x7DD) — 서울 항구 · 시나리오 기억
///     heroes.png         14x6     주인공 여섯 날바이트(SDATA.DAT 0x2427) — 시나리오 기억
///     misc.png           93x1     함대 뒤 덩이(SDATA.DAT 0x48D9) — 물 · 식량 비율 · 동료 칸
///     items.png          20x100   아이템 백(SDATA.DAT 0x55B6) — 이름 · 값 · 힘 · 갈래
///     discoveries.png    7x100    발견물 백(SDATA.DAT 0x5D86) — 자리 · 어려움 · 갈래
///     patrons.png        22x20    「NPC」 건물 임자 스물(SDATA.DAT 0xAF7) — 귀족 · 학자 · 왕
///     goods.json                  교역품 이름 마흔여섯 (KOUKAI2-han8.exe)
///     menus.json                  고르기 목록 예순하나 (MENU.DAT)
///     snr/SNR0~6.DAT · .MES       시나리오 바이트코드 · 글(그대로 옮겨 둔 것)
///     snr/MESSAGE.DAT             게임 말 1179(그대로)
///     snr/COLONY.DAT              발견물 이름 · 풀이 백(그대로)
///     start-ships.json            선종 본보기 스물다섯(이름 · 노 · 대포 · 자리)과 주인공 첫 선종(SNR1~6.DAT)
///     markets.png        128x13   시장 열셋(값 = 날바이트, SDATA.DAT 0x605A)
///     portstate.png      37x100   항구 형편 백(값 = 날바이트, SDATA.DAT 0x66DA)
///     wind.png           30x45    표 셋을 세로로 쌓음(값 = 날바이트)
///     world/
///       cells.png        1080x540 값 = 칸 값
///       chips.png        256x128  값 = 색인 0~15  (칩 128장)
///       quad.png         256x4    값 = 칩 번호    (그림표)
///       route.png        16x622   뱃길 마디(값 = 날바이트, DATA1.LZW 4번 청크의 쓰는 마디만)
///     port/
///       maps.png         1056x960 96x96 지도 101장을 11x10 으로 깔았다(값 = 칩 번호)
///       chips-0..6.png   256x240  값 = 색인 0~15  (벌마다 칩 240장)
///       chipno.png       100x1    값 = 벌 번호
///       walk.png         4x7      벌마다 걸음 경계 [0, a, b, c]
///       doors.png        24x101   항구마다 문 열둘 (x, y) — ZA_DAT.DAT
///     kao/
///       faces.png        1024x640 얼굴 64x80 백스물여덟을 16 x 8 로(값 = 색인 0~7)
///       finds.png        768x384  발견물 48x48 백스물여덟을 16 x 8 로
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

        BakeRoute(Find("DATA1.LZW"), outDir);

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

        BakeWalk(Find("PORTCHIP.LZW"), outDir);
        BakeDoors(Find("ZA_DAT.DAT"), outDir);
        if (File.Exists(Find("MENU.DAT"))) BakeMenus(Find("MENU.DAT"), outDir);
        BakeScenario(gameDirectory, outDir);
        if (File.Exists(Find("KAO.LZW"))) BakeKao(Find("KAO.LZW"), outDir);

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

        // 새 게임 자료 — 항해 셈에 드는 선종 · 사람 표.
        string sdata = Find("SDATA.DAT");
        if (File.Exists(sdata)) BakeStart(sdata, outDir);

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

    /// <summary>뱃길 그래프를 굽는다 — 쓰는 마디만, 줄 하나가 마디 하나다.</summary>
    public static void BakeRoute(string data1Path, string outDir)
    {
        var chunks = LsArchive.ReadAll(data1Path);
        int count = BitConverter.ToInt16(chunks[5], 0);
        Directory.CreateDirectory(Path.Combine(outDir, "world"));
        IndexedPng.Write(Path.Combine(outDir, "world", "route.png"), chunks[4][..(count * RouteGraph.NodeSize)],
                         RouteGraph.NodeSize, count, DataPalette());
    }

    /// <summary><c>PORTCHIP.LZW</c> 의 걸음 경계만 굽는다(<c>port/walk.png</c>).</summary>
    public static void BakeWalk(string portChipPath, string outDir)
    {
        var sets = PortChipSets.Load(portChipPath);
        var walk = new byte[4 * PortChipSets.SetCount];
        for (int s = 0; s < PortChipSets.SetCount; s++) sets.ExtraOf(s).AsSpan(0, 4).CopyTo(walk.AsSpan(s * 4));
        IndexedPng.Write(Path.Combine(outDir, "port", "walk.png"), walk, 4, PortChipSets.SetCount, DataPalette());
    }

    /// <summary>얼굴 · 발견물 그림 한 줄에 놓는 장 수.</summary>
    public const int KaoCols = 16;

    /// <summary><c>KAO.LZW</c> 를 얼굴 판 · 발견물 판으로 굽는다(<see cref="KaoImages"/>).</summary>
    public static void BakeKao(string kaoPath, string outDir)
    {
        var chunks = LsArchive.ReadAll(kaoPath);
        var faces = chunks.Where(c => c.Length == KaoImages.FaceBytes)
                          .Select(c => KaoImages.Decode(c, KaoImages.FaceW, KaoImages.FaceH)).ToArray();
        var finds = chunks.Where(c => c.Length == KaoImages.FindBytes)
                          .Select(c => KaoImages.Decode(c, KaoImages.FindW, KaoImages.FindH)).ToArray();
        Directory.CreateDirectory(Path.Combine(outDir, "kao"));
        var pal = GamePalette.SeaScreenRgb;
        var fa = KaoImages.Atlas(faces, KaoImages.FaceW, KaoImages.FaceH, KaoCols, out int fw, out int fh);
        IndexedPng.Write(Path.Combine(outDir, "kao", "faces.png"), fa, fw, fh, pal);
        var na = KaoImages.Atlas(finds, KaoImages.FindW, KaoImages.FindH, KaoCols, out int nw, out int nh);
        IndexedPng.Write(Path.Combine(outDir, "kao", "finds.png"), na, nw, nh, pal);
    }

    /// <summary>교역 표를 굽는다 — 이름은 한글판 EXE 에서, 시장 · 항구 형편은 <c>SDATA.DAT</c> 에서.</summary>
    public static void BakeEconomy(string sdataPath, string han8ExePath, string outDir)
    {
        var e = Economy.Load(sdataPath, han8ExePath);
        WriteJson(Path.Combine(outDir, "goods.json"), e.GoodsNames.ToArray());
        IndexedPng.Write(Path.Combine(outDir, "markets.png"), e.MarketBytes, Economy.MarketSize, Economy.MarketCount, DataPalette());
        IndexedPng.Write(Path.Combine(outDir, "portstate.png"), e.StateBytes, Economy.StateSize, Economy.StateCount, DataPalette());
    }

    /// <summary>시나리오 파일(SNR0~6.DAT · .MES)을 <c>snr/</c> 에 그대로 옮긴다.</summary>
    public static void BakeScenario(string gameDirectory, string outDir)
    {
        Directory.CreateDirectory(Path.Combine(outDir, "snr"));
        for (int n = 0; n <= 6; n++)
            foreach (string ext in (string[])["DAT", "MES"])
            {
                string src = Path.Combine(gameDirectory, $"SNR{n}.{ext}");
                if (File.Exists(src)) File.Copy(src, Path.Combine(outDir, "snr", $"SNR{n}.{ext}"), true);
            }
        string msg = Path.Combine(gameDirectory, "MESSAGE.DAT");
        if (File.Exists(msg)) File.Copy(msg, Path.Combine(outDir, "snr", "MESSAGE.DAT"), true);
        string colony = Path.Combine(gameDirectory, "COLONY.DAT");
        if (File.Exists(colony)) File.Copy(colony, Path.Combine(outDir, "snr", "COLONY.DAT"), true);
    }

    /// <summary><c>MENU.DAT</c> 목록을 굽는다.</summary>
    public static void BakeMenus(string menuPath, string outDir) =>
        WriteJson(Path.Combine(outDir, "menus.json"), MenuTable.Load(menuPath).Lists.Select(l => l.ToArray()).ToArray());

    /// <summary><c>ZA_DAT.DAT</c> 의 문 자리를 굽는다(<c>port/doors.png</c>, 줄 하나가 항구 하나).</summary>
    public static void BakeDoors(string zaDatPath, string outDir)
    {
        var doors = PortDoors.Load(zaDatPath);
        IndexedPng.Write(Path.Combine(outDir, "port", "doors.png"), doors.Raw[..PortDoors.Bytes],
                         PortDoors.PerPort * 2, PortDoors.Ports, DataPalette());
    }

    /// <summary><c>SDATA.DAT</c> 의 선종 · 사람 표만 굽는다(<c>ships.json</c> · <c>people.json</c>).</summary>
    public static void BakeStart(string sdataPath, string outDir)
    {
        PortTable.RegisterEncodings();
        var start = StartData.Load(sdataPath);
        WriteJson(Path.Combine(outDir, "ships.json"), start.ShipTypes.Select(ShipTypeJson.Of).ToArray());
        WriteJson(Path.Combine(outDir, "people.json"), start.People.Select(PersonJson.Of).ToArray());
        IndexedPng.Write(Path.Combine(outDir, "fleets.png"), start.FleetBytes,
                         StartData.FleetSize, StartData.FleetCount, DataPalette());
        IndexedPng.Write(Path.Combine(outDir, "people-raw.png"), start.PeopleBytes,
                         StartData.PersonSize, StartData.PersonCount, DataPalette());
        IndexedPng.Write(Path.Combine(outDir, "nations.png"), start.NationBytes,
                         StartData.NationSize, StartData.NationCount, DataPalette());
        IndexedPng.Write(Path.Combine(outDir, "heroes.png"), start.HeroBytes,
                         StartData.HeroSize, StartData.HeroCount, DataPalette());
        IndexedPng.Write(Path.Combine(outDir, "misc.png"), start.MiscBytes, StartData.MiscSize, 1, DataPalette());
        IndexedPng.Write(Path.Combine(outDir, "items.png"), start.ItemBytes, StartData.ItemSize, StartData.ItemCount, DataPalette());
        IndexedPng.Write(Path.Combine(outDir, "discoveries.png"), start.DiscoveryBytes,
                         StartData.DiscoverySize, StartData.DiscoveryCount, DataPalette());
        IndexedPng.Write(Path.Combine(outDir, "patrons.png"), start.PatronBytes,
                         StartData.PatronSize, StartData.PatronCount, DataPalette());
        string? game = Path.GetDirectoryName(sdataPath);
        var heroShips = game != null ? Scenario.HeroShips(game) : [];
        var heroGold = game != null ? Scenario.HeroGold(game) : [];
        WriteJson(Path.Combine(outDir, "start-ships.json"), new StartShipsJson([.. start.Templates], heroShips, heroGold));
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
            string facesPath = Path.Combine(dir, "kao", "faces.png");
            IndexedPng.Image? faces = File.Exists(facesPath) ? IndexedPng.Read(facesPath) : null;
            string findsPath = Path.Combine(dir, "kao", "finds.png");
            IndexedPng.Image? finds = File.Exists(findsPath) ? IndexedPng.Read(findsPath) : null;
            string doorsPath = Path.Combine(dir, "port", "doors.png");
            var doors = File.Exists(doorsPath) ? PortDoors.FromBytes(IndexedPng.Read(doorsPath).Pixels) : null;
            string walkPath = Path.Combine(dir, "port", "walk.png");
            var walkBounds = File.Exists(walkPath)
                ? IndexedPng.Read(walkPath).Pixels.Chunk(4).Select(b => WalkBounds.FromBytes(b)).ToArray()
                : [];
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

            var shipTypes = ReadJson<ShipTypeJson[]>(Path.Combine(dir, "ships.json"));
            var people = ReadJson<PersonJson[]>(Path.Combine(dir, "people.json"));
            string routePath = Path.Combine(dir, "world", "route.png");
            RouteGraph? routes = null;
            if (File.Exists(routePath))
            {
                var r = IndexedPng.Read(routePath);
                routes = RouteGraph.FromBytes(r.Pixels, r.Height);
            }
            var menuLists = ReadJson<string[][]>(Path.Combine(dir, "menus.json"));
            var menus = menuLists != null ? MenuTable.FromLists(menuLists) : null;
            Economy? economy = null;
            var goodsNames = ReadJson<string[]>(Path.Combine(dir, "goods.json"));
            string marketsPath = Path.Combine(dir, "markets.png"), statePath = Path.Combine(dir, "portstate.png");
            if (goodsNames != null && File.Exists(marketsPath) && File.Exists(statePath))
                economy = Economy.FromParts(goodsNames, IndexedPng.Read(marketsPath).Pixels, IndexedPng.Read(statePath).Pixels);
            string fleetsPath = Path.Combine(dir, "fleets.png");
            byte[]? fleetBytes = File.Exists(fleetsPath) ? IndexedPng.Read(fleetsPath).Pixels : null;
            string peoplePath = Path.Combine(dir, "people-raw.png");
            byte[]? peopleBytes = File.Exists(peoplePath) ? IndexedPng.Read(peoplePath).Pixels : null;
            string nationsPath = Path.Combine(dir, "nations.png");
            byte[]? nationBytes = File.Exists(nationsPath) ? IndexedPng.Read(nationsPath).Pixels : null;
            string heroesPath = Path.Combine(dir, "heroes.png");
            byte[]? heroBytes = File.Exists(heroesPath) ? IndexedPng.Read(heroesPath).Pixels : null;
            string miscPath = Path.Combine(dir, "misc.png");
            byte[]? miscBytes = File.Exists(miscPath) ? IndexedPng.Read(miscPath).Pixels : null;
            string itemsPath = Path.Combine(dir, "items.png");
            byte[]? itemBytes = File.Exists(itemsPath) ? IndexedPng.Read(itemsPath).Pixels : null;
            string discPath = Path.Combine(dir, "discoveries.png");
            byte[]? discBytes = File.Exists(discPath) ? IndexedPng.Read(discPath).Pixels : null;
            string patronPath = Path.Combine(dir, "patrons.png");
            byte[]? patronBytes = File.Exists(patronPath) ? IndexedPng.Read(patronPath).Pixels : null;
            var startShips = ReadJson<StartShipsJson>(Path.Combine(dir, "start-ships.json"));
            var start = shipTypes != null && people != null
                ? StartData.FromTables([.. shipTypes.Select(t => t.To())], [.. people.Select(p => p.To())], fleetBytes,
                                       startShips?.Templates, startShips?.HeroShips, startShips?.HeroGold, peopleBytes, nationBytes, heroBytes, miscBytes, itemBytes, discBytes, patronBytes)
                : null;

            return new AssetPack
            {
                Start = start,
                Routes = routes,
                Economy = economy,
                Menus = menus,
                Cells = cells.Pixels,
                WorldAtlas = chips.Pixels,
                WorldAtlasSize = (chips.Width, chips.Height),
                Quads = quad.Pixels,
                PortMaps = UntilePortMaps(maps),
                PortAtlases = sets,
                PortAtlasSize = (AtlasCols * ChipSheet.Size,
                                 PortChipSets.ChipsPerSet / AtlasCols * ChipSheet.Size),
                PortChipSet = chipNo.Pixels,
                PortWalkBounds = walkBounds,
                Doors = doors,
                Faces = faces?.Pixels ?? [],
                FacesWidth = faces?.Width ?? 0,
                Finds = finds?.Pixels ?? [],
                FindsWidth = finds?.Width ?? 0,
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

    /// <summary>얼굴 판(<c>kao/faces.png</c>) — 64 x 80 을 <see cref="KaoCols"/> 장씩. 없으면 빈 것.</summary>
    public byte[] Faces { get; private init; } = [];
    public int FacesWidth { get; private init; }

    /// <summary>얼굴 하나가 판의 어디인지.</summary>
    public static (int X, int Y) FaceAt(int face) =>
        (face % KaoCols * KaoImages.FaceW, face / KaoCols * KaoImages.FaceH);

    /// <summary>발견물 그림 판(<c>kao/finds.png</c>) — 48 x 48 을 <see cref="KaoCols"/> 장씩. 없으면 빈 것.</summary>
    public byte[] Finds { get; private init; } = [];
    public int FindsWidth { get; private init; }

    /// <summary>항구마다 문 자리. 구워 두지 않았으면 null.</summary>
    public PortDoors? Doors { get; private init; }

    /// <summary>칩 벌마다 걸음 경계. 구워 두지 않았으면 빈 것.</summary>
    public WalkBounds[] PortWalkBounds { get; private init; } = [];

    /// <summary>바람·해류 날바이트 1,350.</summary>
    public byte[] Wind { get; private init; } = [];

    /// <summary>고르기 목록. 구워 두지 않았으면 null.</summary>
    public MenuTable? Menus { get; private init; }

    /// <summary>교역 표. 구워 두지 않았으면 null.</summary>
    public Economy? Economy { get; private init; }

    /// <summary>뱃길 그래프. 구워 두지 않았으면 null.</summary>
    public RouteGraph? Routes { get; private init; }

    /// <summary>선종 · 사람 표. 구워 두지 않았으면 null.</summary>
    public StartData? Start { get; private init; }

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

    private record StartShipsJson(
        [property: JsonPropertyName("본보기")] StartData.ShipTemplate[] Templates,
        [property: JsonPropertyName("주인공첫배")] int[] HeroShips,
        [property: JsonPropertyName("주인공첫돈")] int[]? HeroGold = null);

    private record ShipTypeJson(
        [property: JsonPropertyName("번호")] int Index,
        [property: JsonPropertyName("+0")] int Field0,
        [property: JsonPropertyName("내구")] int Durability,
        [property: JsonPropertyName("역풍")] int Upwind,
        [property: JsonPropertyName("돛")] int Sail,
        [property: JsonPropertyName("+4")] int Field4,
        [property: JsonPropertyName("드는선원")] int RequiredCrew,
        [property: JsonPropertyName("짐칸")] int Capacity,
        [property: JsonPropertyName("최대선원")] int MaxCrew,
        [property: JsonPropertyName("돛갈래")] int SailKind,
        [property: JsonPropertyName("값")] int Price)
    {
        public static ShipTypeJson Of(StartData.ShipType t) =>
            new(t.Index, t.Field0, t.Durability, t.Upwind, t.Sail, t.Field4, t.RequiredCrew, t.Capacity,
                t.MaxCrew, t.SailKind, t.Price);

        public StartData.ShipType To() =>
            new(Index, Field0, Durability, Upwind, Sail, Field4, RequiredCrew, Capacity, MaxCrew, SailKind, Price);
    }

    private record PersonJson(
        [property: JsonPropertyName("번호")] int Index,
        [property: JsonPropertyName("이름")] string Name,
        [property: JsonPropertyName("성")] string Family,
        [property: JsonPropertyName("능력")] int[] Stats,
        [property: JsonPropertyName("항해레벨")] int NavLevel,
        [property: JsonPropertyName("전투레벨")] int BattleLevel)
    {
        public static PersonJson Of(StartData.Person p) =>
            new(p.Index, p.Name, p.Family, p.Stats, p.NavLevel, p.BattleLevel);

        /// <summary>항해술은 능력치 둘째(+0x1D)다.</summary>
        public StartData.Person To() =>
            new(Index, Name, Family, Stats, Stats.Length > 1 ? Stats[1] : 0, NavLevel, BattleLevel);
    }
}
