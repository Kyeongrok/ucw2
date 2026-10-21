using Uw2.Support.Local.Formats;
using Xunit;

namespace Uw2.Tests;

public class NpkTests
{
    [Fact]
    public void Unpack_literal_then_repeat_and_row_copy()
    {
        // 표지 0b110 — 날 토막, 네 바이트 앞 되풀이 한 토막, 다음 줄은 한 줄 위를 두 토막(0x81).
        byte[] src = [0b110, 0x88, 0x00, 0x00, 0x81];
        var o = Npk.Unpack(src, 8, 2);
        Assert.Equal(new byte[] { 12, 0, 0, 0, 12, 0, 0, 0, 12, 0, 0, 0, 12, 0, 0, 0 }, o);
    }

    [Fact]
    public void Decode_reads_size_and_palette()
    {
        var d = new byte[Npk.HeaderSize + 3];
        "NPK016"u8.CopyTo(d);
        d[0x0C] = 4; d[0x0E] = 1;
        d[0x10 + 2] = 0x5A; d[0x10 + 3] = 0x03;     // 빛깔 1 = G 3 · R 5 · B 10
        d[Npk.HeaderSize] = 0; d[Npk.HeaderSize + 1] = 0x08; d[Npk.HeaderSize + 2] = 0;
        var p = Npk.Decode(d);
        Assert.Equal((4, 1), (p.Width, p.Height));
        Assert.Equal(new byte[] { 4, 0, 0, 0 }, p.Pixels);
        Assert.Equal(new byte[] { 85, 51, 170 }, p.Palette[3..6]);
    }
}
