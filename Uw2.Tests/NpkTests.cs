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

    [Fact]
    public void DuelSprite_part_blits_four_planes_per_eight_dots_and_skips_colour_8()
    {
        // 조각 하나 — x 1(8 점), y 0, 가로 1 바이트, 세로 1, 몸은 자리 16 부터. 끝 표시(세로 0xFF).
        byte[] chunk = [1, 0, 1, 1, 0, 0, 0, 16, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF,
                        0b1000_0001, 0b0000_0001, 0b0000_0000, 0b1010_0001];   // 면 0 ~ 3
        var parts = DuelSprites.Parts(chunk);
        Assert.Single(parts);
        var cell = new byte[16];
        Array.Fill(cell, (byte)0xEE);
        DuelSprites.Blit(chunk, parts[0], cell, 16);
        Assert.Equal(9, cell[8]);            // 왼쪽 점 — 면 0 · 3
        Assert.Equal(0, cell[9]);
        Assert.Equal(0xEE, cell[10]);        // 색 8(면 3 만)은 비친다
        Assert.Equal(11, cell[15]);          // 오른쪽 점 — 면 0 · 1 · 3
    }
}
