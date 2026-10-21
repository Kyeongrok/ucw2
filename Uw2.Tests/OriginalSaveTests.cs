using System.Text;
using Uw2.Support.Local.Formats;
using Xunit;

namespace Uw2.Tests;

/// <summary>원본 세이브 읽기(<see cref="OriginalSave"/>) — 머리 0x97 + 칸 0x837B x 10.</summary>
public class OriginalSaveTests
{
    private static byte[] Make()
    {
        var d = new byte[OriginalSave.HeadSize + OriginalSave.SlotSize * OriginalSave.SlotCount];
        Encoding.ASCII.GetBytes(" 2/Aug/1522").CopyTo(d, 1 + 2 * OriginalSave.LabelSize);
        int at = OriginalSave.HeadSize + 2 * OriginalSave.SlotSize;
        d[at + 5] = 21; d[at + 6] = 7; d[at + 7] = 1; d[at + 8] = 58; d[at + 9] = 0xFF;
        BitConverter.GetBytes(123456).CopyTo(d, at + OriginalSave.GoldOffset);
        BitConverter.GetBytes((ushort)7).CopyTo(d, at + OriginalSave.GoldOffset + 4);
        d[at + OriginalSave.GoldOffset + 6] = 42;
        d[at + OriginalSave.GoldOffset + 7] = 3;
        return d;
    }

    [Fact]
    public void Reads_only_labelled_slots_and_their_header()
    {
        var save = OriginalSave.Parse(Make());
        Assert.Single(save.Slots, s => s != null);
        var slot = save.Slots[2]!;
        Assert.Equal("2/Aug/1522", slot.Label);
        Assert.Equal((21, 7, 1, 58, -1), (slot.Year, slot.Month, slot.Day, slot.Time, slot.Port));
        Assert.Equal(123456, slot.Gold);
        Assert.Equal(742, slot.Balance);
        Assert.Equal(3, slot.HeroPerson);
    }

    [Fact]
    public void Pointer_maps_memory_address_to_table()
    {
        Assert.Equal((3, 50 * 2 + 0x2C), OriginalSave.Pointer(0x0045A240 + 50 * 2 + 0x2C));
        Assert.Equal((8, 0x85), OriginalSave.Pointer(0x0045BE08 + 0x85));
        Assert.Null(OriginalSave.Pointer(0x00400000));
    }

    [Fact]
    public void Short_file_is_rejected() =>
        Assert.Throws<InvalidDataException>(() => OriginalSave.Parse(new byte[100]));
}
