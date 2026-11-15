using TICPatcher.Core.Memory;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Tests;

// All data here is generated: no game data is used or shipped.
public class TableTests
{
    private static readonly TableSchema JobSchema = Jobs.Schema;

    // A made-up job table with plausible values (Move/Jump 3-6 etc.).
    private static byte[] FakeJobTable(int seed, int recordSize = 49)
    {
        var random = new Random(seed);
        var table = new byte[JobSchema.RecordCount * recordSize];
        random.NextBytes(table);
        for (var job = 0; job < JobSchema.RecordCount; job++)
        {
            table[job * recordSize + 24] = (byte)random.Next(3, 7); // Move
            table[job * recordSize + 25] = (byte)random.Next(2, 5); // Jump
        }
        return table;
    }

    [Fact]
    public void ReadsAndWritesNumbersAndFlags()
    {
        var record = new byte[JobSchema.RecordSize];
        RecordCodec.Write(JobSchema.Field("Move"), record, "6");
        RecordCodec.Write(JobSchema.Field("InnateA"), record, "474");
        RecordCodec.Write(JobSchema.Field("Equipment"), record, "Knife, Sword, FellSword");
        RecordCodec.Write(JobSchema.Field("AbsorbElement"), record, "Fire, Dark");

        Assert.Equal(6, record[24]);
        Assert.Equal(0xDA, record[1]);
        Assert.Equal(0x01, record[2]);
        Assert.Equal(0b0101_0000, record[9]);   // Knife (bit 6), Sword (bit 4)
        Assert.Equal(0b0001_0000, record[13]);  // FellSword: 4th bit of the 5th equipment byte
        Assert.Equal(0b1000_0001, record[42]);  // Fire = 0x80, Dark = 0x01

        Assert.Equal("6", RecordCodec.Read(JobSchema.Field("Move"), record));
        Assert.Equal("474", RecordCodec.Read(JobSchema.Field("InnateA"), record));
        Assert.Equal("Knife, Sword, FellSword", RecordCodec.Read(JobSchema.Field("Equipment"), record));
        Assert.Equal("Fire, Dark", RecordCodec.Read(JobSchema.Field("AbsorbElement"), record));
    }

    [Fact]
    public void UnknownFlagBitsAreKept()
    {
        var record = new byte[JobSchema.RecordSize];
        record[13] = 0b1000_0000; // an unknown bit in the 5th equipment byte
        RecordCodec.Write(JobSchema.Field("Equipment"), record, "LipRouge");
        Assert.Equal(0b1000_1000, record[13]);
        Assert.Throws<ArgumentException>(() => RecordCodec.Write(JobSchema.Field("Equipment"), record, "Spoon"));
    }

    [Fact]
    public void FindsTheTableInMemoryEvenWithRebalancedValues()
    {
        var reference = FakeJobTable(seed: 1);
        var remaster = (byte[])reference.Clone();
        // The remaster changed some stats and a few Move values.
        var random = new Random(2);
        for (var i = 0; i < 300; i++)
            remaster[random.Next(remaster.Length)] = (byte)random.Next(256);

        var memory = new byte[2_000_000];
        new Random(3).NextBytes(memory);
        const int at = 1_234_567;
        remaster.CopyTo(memory, at);

        var best = TableLocator.ForJobs(reference).Find(memory).First();
        Assert.Equal(at, best.Offset);
        Assert.Equal(49, best.RecordSize);
        Assert.True(best.Score > 0.9, $"score {best.Score}");
    }

    [Fact]
    public void FindsATableWhoseRecordsGrew()
    {
        var reference = FakeJobTable(seed: 4);
        // Same records, each padded to 52 bytes in the remaster.
        var grown = new byte[JobSchema.RecordCount * 52];
        for (var job = 0; job < JobSchema.RecordCount; job++)
            Array.Copy(reference, job * 49, grown, job * 52, 49);

        var memory = new byte[500_000];
        new Random(5).NextBytes(memory);
        grown.CopyTo(memory, 100_000);

        var best = TableLocator.ForJobs(reference).Find(memory).First();
        Assert.Equal(100_000, best.Offset);
        Assert.Equal(52, best.RecordSize);
    }

    [Fact]
    public void FindsNothingInRandomMemory()
    {
        var memory = new byte[1_000_000];
        new Random(6).NextBytes(memory);
        Assert.Empty(TableLocator.ForJobs(FakeJobTable(seed: 7)).Find(memory));
    }
}
