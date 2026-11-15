using TICPatcher.Core.Memory;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Tests;

// Generated data only.
public class LoaderTests
{
    private static byte[] TableWithFingerprint()
    {
        var table = new byte[Jobs.Schema.RecordCount * Jobs.Schema.RecordSize];
        new Random(10).NextBytes(table);
        for (var job = 0; job < Jobs.Schema.RecordCount; job++)
            table[job * Jobs.Schema.RecordSize] = 0; // keep stray 5s out of skill set slots
        for (var job = Jobs.FirstGenericJob; job <= Jobs.LastGenericJob; job++)
            table[job * Jobs.Schema.RecordSize] = (byte)(5 + job - Jobs.FirstGenericJob);
        return table;
    }

    [Fact]
    public void FingerprintFindsTheJobTable()
    {
        var memory = new byte[3_000_000];
        new Random(11).NextBytes(memory);
        TableWithFingerprint().CopyTo(memory, 2_000_001);
        Assert.Equal([2_000_001L], JobTableFingerprint.Find(memory));
    }

    [Fact]
    public void FingerprintIgnoresRandomMemory()
    {
        var memory = new byte[3_000_000];
        new Random(12).NextBytes(memory);
        Assert.Empty(JobTableFingerprint.Find(memory));
    }

    [Fact]
    public void ParsesValidatesMergesAndApplies()
    {
        var first = TableEdits.Parse("""
            // a TIC mod
            { "Jobs": { "0x01": { "Move": 6, "Jump": "4" }, "74": { "Equipment": "Knife, Sword" } } }
            """);
        var second = TableEdits.Parse("""{ "Jobs": { "0x01": { "Move": "7" } } }""");
        first.Merge(second);

        var table = TableWithFingerprint();
        var applied = first.ApplyTo("Jobs", table, Jobs.Schema.RecordSize);
        Assert.Equal(3, applied);

        var ramza = table.AsSpan(1 * Jobs.Schema.RecordSize, Jobs.Schema.RecordSize).ToArray();
        Assert.Equal("7", RecordCodec.Read(Jobs.Schema.Field("Move"), ramza)); // later mod wins
        Assert.Equal("4", RecordCodec.Read(Jobs.Schema.Field("Jump"), ramza));
        var squire = table.AsSpan(0x4A * Jobs.Schema.RecordSize, Jobs.Schema.RecordSize).ToArray();
        Assert.Equal("Knife, Sword", RecordCodec.Read(Jobs.Schema.Field("Equipment"), squire));
    }

    [Theory]
    [InlineData("""{ "Items": { "0x01": { "Price": 1 } } }""", "Unknown table")]
    [InlineData("""{ "Jobs": { "0xFF": { "Move": 6 } } }""", "outside")]
    [InlineData("""{ "Jobs": { "0x01": { "Speed": 6 } } }""", "unknown field")]
    [InlineData("""{ "Jobs": { "0x01": { "Move": 300 } } }""", "isn't valid")]
    [InlineData("""{ "Jobs": { "0x01": { "Equipment": "Spoon" } } }""", "isn't valid")]
    [InlineData("""{ "Jobs": { "Ramza": { "Move": 6 } } }""", "isn't a record id")]
    public void RejectsBadModsWithClearMessages(string json, string expected)
    {
        var error = Assert.Throws<FormatException>(() => TableEdits.Parse(json));
        Assert.Contains(expected, error.Message);
    }
}
