using TICLoader.Interfaces;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Tests;

// Generated data only. Covers what a bridge relies on: mod formats, edits
// from code, raw bytes and load order.
public sealed class BridgeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tic-bridge-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Mod(string id, string? tablesJson = null, string? otherFile = null)
    {
        var folder = Path.Combine(_root, id);
        Directory.CreateDirectory(Path.Combine(folder, "TIC"));
        if (tablesJson is not null)
            File.WriteAllText(Path.Combine(folder, ModCollector.TablesFile), tablesJson);
        if (otherFile is not null)
            File.WriteAllText(Path.Combine(folder, "other.txt"), otherFile);
        return folder;
    }

    // A made-up format: other.txt with lines "Table Id Field Value".
    private sealed class LineFormat : ITICModFormat
    {
        public string Name => "line format";

        public IReadOnlyList<TICFieldEdit>? Read(string modId, string modFolder)
        {
            var path = Path.Combine(modFolder, "other.txt");
            if (!File.Exists(path))
                return null;
            return File.ReadAllLines(path).Select(line => line.Split(' ', 4) is [var t, var i, var f, var v]
                ? new TICFieldEdit(t, Convert.ToInt32(i, 16), f, v)
                : throw new FormatException($"bad line \"{line}\"")).ToList();
        }
    }

    [Fact]
    public void FormatsCodeAndTablesJsonMergeInLoadOrder()
    {
        var a = Mod("a", tablesJson: """{ "Jobs": { "0x01": { "Move": 5 } } }""");
        var b = Mod("b", otherFile: "Jobs 01 Move 7");
        var collector = new ModCollector();
        collector.AddFormat(new LineFormat());
        collector.AddEdits("a", [new TICFieldEdit("Jobs", 0x01, "Jump", "4")]);
        var log = new List<string>();

        var edits = collector.Collect([("a", a), ("b", b)], log.Add);

        Assert.Equal("7", edits.Get("Jobs", 0x01, "Move")); // b is later
        Assert.Equal("4", edits.Get("Jobs", 0x01, "Jump"));
        Assert.Contains(log, l => l.StartsWith("b:") && l.Contains("line format"));
    }

    [Fact]
    public void ABrokenModIsSkippedAndTheRestLoad()
    {
        var bad = Mod("bad", otherFile: "nonsense");
        var good = Mod("good", tablesJson: """{ "Jobs": { "0x01": { "Move": 6 } } }""");
        var collector = new ModCollector();
        collector.AddFormat(new LineFormat());
        var log = new List<string>();

        var edits = collector.Collect([("bad", bad), ("good", good)], log.Add);

        Assert.Equal("6", edits.Get("Jobs", 0x01, "Move"));
        Assert.Contains(log, l => l.StartsWith("bad: skipped line format") && l.Contains("bad line"));
    }

    [Fact]
    public void RawBytesApplyAfterNamedFieldsAndRoundTrip()
    {
        var edits = TableEdits.Parse("""{ "ActionAbilities": { "0x92": { "+14": 1, "MPCost": 3, "+13": 9 } } }""");
        var table = new byte[Abilities.Action.Length];
        Assert.Equal(3, edits.ApplyTo("ActionAbilities", table, Abilities.Action.RecordSize));
        var record = 0x92 * Abilities.Action.RecordSize;
        Assert.Equal(1, table[record + 14]);
        Assert.Equal(9, table[record + 13]); // raw +13 wins over MPCost (byte 13)

        var json = edits.ToJson();
        Assert.Contains("\"+14\": 1", json);
        Assert.Equal(edits.All.ToHashSet(), TableEdits.Parse(json).All.ToHashSet());

        Assert.Throws<FormatException>(() => TableEdits.Parse("""{ "ActionAbilities": { "0x92": { "+20": 1 } } }"""));
        Assert.Throws<FormatException>(() => TableEdits.Parse("""{ "ActionAbilities": { "0x92": { "+1": 256 } } }"""));
    }

    [Fact]
    public void TheApiDescribesEveryTable()
    {
        var tables = ModCollector.DescribeTables();
        Assert.Equal(AllTables.List.Count, tables.Count);
        var move = tables.Single(t => t.Name == "Jobs").Fields.Single(f => f.Name == "Move");
        Assert.Equal((24, 1, "Byte"), (move.Offset, move.Size, move.Kind));
    }
}
