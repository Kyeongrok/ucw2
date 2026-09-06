using Uw2.Support.Local.Formats;
using Uw2.Support.Local.Helpers;

namespace Uw2.Tools;

/// <summary>
/// 자산을 뜯어 보고 구워 내는 손도구. 화면 없이 돌아간다.
/// </summary>
/// <remarks>
/// <code>
///   uw2 list   &lt;폴더&gt;              묶음마다 청크 목록을 적는다
///   uw2 unpack &lt;파일&gt; &lt;낼 곳&gt;      청크를 풀어 .bin 으로 뽑는다
///   uw2 map    &lt;폴더&gt; &lt;낼 곳.bmp&gt;  세계지도를 1080x540 그림으로 뽑는다
///   uw2 chips  &lt;파일&gt; &lt;낼 곳.bmp&gt;  칩 벌을 늘어놓은 그림으로 뽑는다
///   uw2 bake   &lt;폴더&gt; &lt;낼 곳&gt;      칸 지도·칩 아틀라스를 자산으로 굽는다
/// </code>
/// BMP 로 내는 것은 딸린 것 없이 손으로 적을 수 있어서다.
/// </remarks>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }
        try
        {
            switch (args[0])
            {
                case "list": return List(Arg(args, 1) ?? GameFolder.Find() ?? ".");
                case "unpack": return Unpack(Need(args, 1), Arg(args, 2) ?? "out");
                case "map": return MapBmp(Need(args, 1), Arg(args, 2) ?? "worldmap.bmp");
                case "chips": return ChipsBmp(Need(args, 1), Arg(args, 2) ?? "chips.bmp");
                case "bake": return Bake(Need(args, 1), Arg(args, 2) ?? "baked");
                case "assets": return Assets(Need(args, 1), Arg(args, 2) ?? "asset", Arg(args, 3));
                case "ports": return Ports(Arg(args, 1) ?? GameFolder.Find() ?? ".");
                case "port": return PortBmp(Arg(args, 1) ?? GameFolder.Find() ?? ".", int.Parse(Arg(args, 2) ?? "0"), Arg(args, 3) ?? "port.bmp");
                default: Usage(); return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"실패: {ex.Message}");
            return 2;
        }
    }

    private static string? Arg(string[] a, int i) => i < a.Length ? a[i] : null;
    private static string Need(string[] a, int i) => Arg(a, i) ?? throw new ArgumentException("인자가 모자랍니다");

    private static void Usage() => Console.WriteLine("""
        uw2 list   <폴더>
        uw2 unpack <파일.LZW> <낼 곳>
        uw2 map    <폴더> <낼 곳.bmp>
        uw2 chips  <파일.LZW> <낼 곳.bmp>
        uw2 bake   <폴더> <낼 곳>
        uw2 ports  <폴더>
        uw2 port   <폴더> <번호> <낼 곳.bmp>
        uw2 assets <폴더> <낼 곳> [덧폴더]
        """);

    /// <summary>게임 폴더에서 자산을 뽑아 굽는다. 이것만 있으면 게임 없이도 돈다.</summary>
    private static int Assets(string dir, string outDir, string? alsoLookIn)
    {
        AssetPack.Bake(dir, outDir, alsoLookIn);
        long bytes = new DirectoryInfo(outDir).EnumerateFiles("*", SearchOption.AllDirectories)
                                              .Sum(f => f.Length);
        int files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length;
        Console.WriteLine($"{outDir} 에 {files}개, {bytes / 1024.0:N0} KB");

        var back = AssetPack.Load(outDir) ?? throw new InvalidDataException("구운 것을 다시 못 읽습니다");
        var map = WorldMap.Load(Path.Combine(dir, "WORLDMAP.LZW"));
        Console.WriteLine($"되읽기 검산 — 칸 {(back.Cells.AsSpan().SequenceEqual(map.Cells) ? "같다" : "다르다!")}"
                          + $" · 항구지도 {back.PortMaps.Length}장 · 항구표 {back.Ports.Count}줄"
                          + $" · 글꼴 {(back.Font != null ? "있다" : "없다")}");
        return 0;
    }

    private static int Ports(string dir)
    {
        PortTable.RegisterEncodings();
        var t = PortTable.Load(dir) ?? throw new InvalidDataException("항구 표를 못 찾았습니다");
        var chipNo = PortChipNumbers.Load(Path.Combine(dir, "CHIP_NO.DAT"));
        Console.WriteLine($"{t.Source} 에서 항구 {t.Ports.Count}곳");
        foreach (var p in t.Ports)
            Console.WriteLine($"  {p.Index,3}  {p.Name,-14} 칩({p.X,5},{p.Y,5})  칸({p.Cell.X,4},{p.Cell.Y,4})  벌 {chipNo[p.Index]}  나라 0x{p.Nation:X2}");
        return 0;
    }

    private static int PortBmp(string dir, int port, string outPath)
    {
        PortTable.RegisterEncodings();
        var maps = PortMap.Load(Path.Combine(dir, "PORTMAP.LZW"));
        var sets = PortChipSets.Load(Path.Combine(dir, "PORTCHIP.LZW"));
        var chipNo = PortChipNumbers.Load(Path.Combine(dir, "CHIP_NO.DAT"));
        var table = PortTable.Load(dir);

        var sheet = sets[chipNo[port]];
        var cells = maps[port];
        var pal = GamePalette.SeaScreen().Bgra;
        int side = PortMap.Size * ChipSheet.Size;
        var px = new uint[side * side];
        for (int cy = 0; cy < PortMap.Size; cy++)
            for (int cx = 0; cx < PortMap.Size; cx++)
            {
                int t = cells[cy * PortMap.Size + cx];
                if (t >= sheet.Count) t = 0;
                for (int y = 0; y < ChipSheet.Size; y++)
                    for (int x = 0; x < ChipSheet.Size; x++)
                        px[(cy * ChipSheet.Size + y) * side + cx * ChipSheet.Size + x] = pal[sheet[t, x, y]];
            }
        WriteBmp(outPath, px, side, side);
        string name = table != null && port < table.Ports.Count ? table.Ports[port].Name : $"항구 {port}";
        Console.WriteLine($"{outPath} — {name}, 벌 {chipNo[port]}, {side}x{side}");
        return 0;
    }

    private static int List(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*.LZW").Order())
        {
            var bytes = File.ReadAllBytes(f);
            var entries = LsArchive.ReadDirectory(bytes, out _);
            long total = entries.Sum(e => (long)e.UnpackedSize);
            Console.WriteLine($"{Path.GetFileName(f),-16} 청크 {entries.Length,3}개  푼 크기 {total:N0}");
            foreach (var (e, i) in entries.Select((e, i) => (e, i)))
                Console.WriteLine($"    {i,3}  눌림 {e.PackedSize,7:N0}  품 {e.UnpackedSize,7:N0}  자리 0x{e.FileOffset:X6}"
                                  + (e.PackedSize == e.UnpackedSize ? "  (안 눌림)" : ""));
        }
        return 0;
    }

    private static int Unpack(string file, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var chunks = LsArchive.ReadAll(file);
        string stem = Path.GetFileNameWithoutExtension(file);
        for (int i = 0; i < chunks.Length; i++)
            File.WriteAllBytes(Path.Combine(outDir, $"{stem}.{i:D2}.bin"), chunks[i]);
        Console.WriteLine($"{chunks.Length}개를 {outDir} 에 냈습니다");
        return 0;
    }

    private static int MapBmp(string dir, string outPath)
    {
        var map = WorldMap.Load(Path.Combine(dir, "WORLDMAP.LZW"));
        var px = new uint[WorldMap.Width * WorldMap.Height];
        for (int y = 0; y < WorldMap.Height; y++)
            for (int x = 0; x < WorldMap.Width; x++)
            {
                byte v = map[x, y];
                px[y * WorldMap.Width + x] = v == WorldMap.Sea ? 0xFF17376Fu
                                           : v == WorldMap.Land ? 0xFFE2D6B0u
                                           : 0xFFB05C40u;
            }
        WriteBmp(outPath, px, WorldMap.Width, WorldMap.Height);
        Console.WriteLine($"{outPath} — {WorldMap.Width}x{WorldMap.Height}");
        return 0;
    }

    private static int ChipsBmp(string file, string outPath)
    {
        var chunks = LsArchive.ReadAll(file);
        var big = chunks.Where(c => c.Length >= 64 * ChipSheet.BytesPerChip && c.Length % ChipSheet.BytesPerChip == 0)
                        .OrderByDescending(c => c.Length).FirstOrDefault()
                  ?? throw new InvalidDataException("칩 벌로 보이는 청크가 없습니다");

        var sheet = ChipSheet.Open(big);
        const int cols = 16;
        var atlas = sheet.ToAtlas(cols, out int w, out int h);
        var pal = GamePalette.SeaScreen().Bgra;
        var px = new uint[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = pal[atlas[i] & 15];
        WriteBmp(outPath, px, w, h);
        Console.WriteLine($"{outPath} — 칩 {sheet.Count}장, {w}x{h}");
        return 0;
    }

    private static int Bake(string dir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var map = WorldMap.Load(Path.Combine(dir, "WORLDMAP.LZW"));
        File.WriteAllBytes(Path.Combine(outDir, "world.cells"), map.Cells);
        Console.WriteLine($"world.cells — {WorldMap.Width}x{WorldMap.Height} = {map.Cells.Length:N0}바이트");

        string wind = Path.Combine(dir, "WINDCUR.DAT");
        if (File.Exists(wind))
        {
            File.Copy(wind, Path.Combine(outDir, "windcur.bin"), overwrite: true);
            Console.WriteLine($"windcur.bin — {WindCurTable.Cols}x{WindCurTable.Rows}");
        }

        var sheet = WorldChips.Load(dir);
        var atlas = sheet.ToAtlas(16, out int w, out int h);
        File.WriteAllBytes(Path.Combine(outDir, "world.atlas"), atlas);
        Console.WriteLine($"world.atlas — 칩 {sheet.Count}장, {w}x{h}, {sheet.Kind}");

        // 눈으로 보라고 칩 벌도 한 장 낸다.
        var pal = GamePalette.SeaScreen().Bgra;
        var px = new uint[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = pal[atlas[i] & 15];
        WriteBmp(Path.Combine(outDir, "world-chips.bmp"), px, w, h);
        Console.WriteLine("world-chips.bmp");
        return 0;
    }

    /// <summary>32비트 BMP 로 적는다. 딸린 것 없이 손으로 적을 수 있어 이것을 쓴다.</summary>
    private static void WriteBmp(string path, uint[] bgra, int w, int h)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        int size = 54 + w * h * 4;
        bw.Write((ushort)0x4D42);
        bw.Write(size);
        bw.Write(0);
        bw.Write(54);
        bw.Write(40);
        bw.Write(w);
        bw.Write(-h);          // 음수면 위에서 아래로 적는다
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);
        bw.Write(w * h * 4);
        bw.Write(2835); bw.Write(2835);
        bw.Write(0); bw.Write(0);
        foreach (var p in bgra) bw.Write(p);
    }
}
