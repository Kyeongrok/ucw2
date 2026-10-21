using Uw2.Game.Engine;
using Xunit;

namespace Uw2.Tests;

/// <summary>항해일지(<see cref="Logbook"/>) — 0x0046E498, 16 바이트 x 48.</summary>
public class LogbookTests
{
    [Fact]
    public void Round_trips_the_original_layout()
    {
        var log = new Logbook();
        log.Add(21, 7, 1, Logbook.Arrived, 11, 4);
        log.Add(21, 7, 2, Logbook.Bought, 3, 1, 0x2345, 20);
        var raw = log.Export();
        Assert.Equal(Logbook.Bytes, raw.Length);
        Assert.Equal((21 * 12 + 7) * 31 + 1, BitConverter.ToUInt16(raw, 0));
        Assert.Equal(0xFFFF, BitConverter.ToUInt16(raw, 2 * Logbook.LineSize));
        var back = new Logbook();
        back.Import(raw);
        Assert.Equal(log.Entries, back.Entries);
    }

    [Fact]
    public void Full_log_drops_the_oldest_line()
    {
        var log = new Logbook();
        for (int i = 0; i < Logbook.Lines + 3; i++) log.Add(0, 0, i % 31, Logbook.Departed, i, 0);
        Assert.Equal(Logbook.Lines, log.Entries.Count);
        Assert.Equal(3, log.Entries[0].A);
    }
}
