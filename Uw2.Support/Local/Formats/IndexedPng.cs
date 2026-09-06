using System.Buffers.Binary;
using System.IO.Compression;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 색인 PNG 읽개·쓰개. <b>여덟 비트 색인 한 장</b>만 다룬다.
/// </summary>
/// <remarks>
/// 자산을 이 꼴로 굽는 까닭은 둘이다.
/// <list type="bullet">
///   <item><b>값이 그대로 남는다.</b> 칸 값이든 칩 번호든 색인 그대로라 읽어 오면 원본이다.</item>
///   <item><b>눈으로 볼 수 있다.</b> 팔레트를 달아 두니 GitHub 에서도 그림으로 뜬다.</item>
/// </list>
/// 딸린 것 없이 짠다 — 압축은 <see cref="ZLibStream"/>, CRC 는 아래 표다.
/// </remarks>
public static class IndexedPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>색인 한 장. <paramref name="Pixels"/> 는 줄 먼저(<c>y * Width + x</c>)다.</summary>
    public readonly record struct Image(int Width, int Height, byte[] Pixels, byte[] Palette);

    /// <summary>색인 그림을 적는다. <paramref name="palette"/> 는 RGB 세 바이트씩이다.</summary>
    public static void Write(string path, ReadOnlySpan<byte> pixels, int width, int height,
                             ReadOnlySpan<byte> palette)
    {
        if (pixels.Length < width * height)
            throw new ArgumentException($"점이 모자랍니다 ({pixels.Length} < {width * height})");

        using var fs = File.Create(path);
        fs.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;      // 비트 깊이
        ihdr[9] = 3;      // 색인 그림
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(fs, "IHDR", ihdr);

        // 팔레트는 256칸을 채운다 — 모자라면 검정으로 메운다.
        var plte = new byte[256 * 3];
        palette[..Math.Min(palette.Length, plte.Length)].CopyTo(plte);
        Chunk(fs, "PLTE", plte);

        // 줄마다 거르개 0 을 앞에 붙여 zlib 로 누른다.
        var raw = new byte[(width + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (width + 1)] = 0;
            pixels.Slice(y * width, width).CopyTo(raw.AsSpan(y * (width + 1) + 1));
        }
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(raw);
        Chunk(fs, "IDAT", packed.ToArray());

        Chunk(fs, "IEND", []);
    }

    /// <summary>색인 그림을 읽는다.</summary>
    public static Image Read(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length < 8 || !d.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("PNG 가 아닙니다");

        int w = 0, h = 0;
        byte[] palette = [];
        using var idat = new MemoryStream();

        int at = 8;
        while (at + 8 <= d.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at));
            string kind = System.Text.Encoding.ASCII.GetString(d, at + 4, 4);
            var body = d.AsSpan(at + 8, len);

            switch (kind)
            {
                case "IHDR":
                    w = BinaryPrimitives.ReadInt32BigEndian(body);
                    h = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    if (body[8] != 8 || body[9] != 3)
                        throw new InvalidDataException("여덟 비트 색인 그림이 아닙니다");
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "IDAT": idat.Write(body); break;
            }
            at += 12 + len;                       // 길이 + 종류 + 알맹이 + CRC
            if (kind == "IEND") break;
        }

        idat.Position = 0;
        var raw = new byte[(w + 1) * h];
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            z.ReadExactly(raw);

        var px = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            if (raw[y * (w + 1)] != 0)
                throw new InvalidDataException("거르개를 쓴 PNG 는 못 읽습니다");
            raw.AsSpan(y * (w + 1) + 1, w).CopyTo(px.AsSpan(y * w));
        }
        return new Image(w, h, px, palette);
    }

    private static void Chunk(Stream s, string kind, ReadOnlySpan<byte> body)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, body.Length);
        s.Write(len);

        var name = System.Text.Encoding.ASCII.GetBytes(kind);
        s.Write(name);
        s.Write(body);

        uint crc = Crc32(name, body);
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (byte x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
