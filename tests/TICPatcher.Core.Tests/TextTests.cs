using System.Text;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Text;

namespace TICPatcher.Core.Tests;

// Synthetic .nxd tables only; no game data in the repo.
public class TextTests
{
    // An NXDF table shaped like the game's: header, (id, offset) index, fixed rows, string pool.
    private static byte[] BuildTable(TextTable table, Func<int, string, string> text)
    {
        var rows = table.Rows;
        var rowLength = table.RowLength > 0 ? table.RowLength : 0x44;
        var indexAt = 0x30;
        var rowsAt = indexAt + rows * 8;
        var poolAt = rowsAt + rows * rowLength;
        var pool = new List<byte> { 0 }; // the game's pools start after a NUL too
        var bytes = new byte[poolAt];
        Encoding.ASCII.GetBytes(NxdTable.Magic).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x30).CopyTo(bytes, 0x20);
        BitConverter.GetBytes(rows).CopyTo(bytes, 0x24);
        var pointers = new List<(int At, int Target)>();
        for (var id = 0; id < rows; id++)
        {
            var entry = indexAt + id * 8;
            var row = rowsAt + id * rowLength;
            BitConverter.GetBytes(id).CopyTo(bytes, entry);
            BitConverter.GetBytes(row - (entry + 4)).CopyTo(bytes, entry + 4);
            foreach (var field in table.Fields)
            {
                pointers.Add((row + field.Offset, poolAt + pool.Count - (row + table.Base)));
                pool.AddRange(Encoding.UTF8.GetBytes(text(id, field.Name)));
                pool.Add(0);
            }
        }
        foreach (var (at, target) in pointers)
            BitConverter.GetBytes(target).CopyTo(bytes, at);
        return [.. bytes, .. pool];
    }

    [Fact]
    public void ReadsTextFromATable()
    {
        var bytes = BuildTable(TextTables.Items, (id, field) => $"{field} {id}");
        var table = NxdTable.Parse(bytes);
        Assert.Equal("Name 253", table.Text(TextTables.Items, 253, TextTables.Items.Field("Name")));
        Assert.Equal("Description 1", table.Text(TextTables.Items, 1, TextTables.Items.Field("Description")));
        Assert.Null(table.Text(TextTables.Items, 999, TextTables.Items.Field("Name")));
    }

    [Fact]
    public void FindsTablesInMemoryAndRenames()
    {
        var items = BuildTable(TextTables.Items, (id, field) => id == 0xFD && field == "Name" ? "Phoenix Down" : $"{field} {id}");
        var names = BuildTable(TextTables.Characters, (id, _) => $"Unit {id}");
        // A block of "memory" holding both tables among other bytes.
        var block = new byte[0x100 + items.Length + 0x40 + names.Length + 0x100];
        new Random(30).NextBytes(block);
        items.CopyTo(block, 0x100);
        names.CopyTo(block, 0x100 + items.Length + 0x40);
        const long blockStart = 0x10_0000_0000;

        var found = TextPatcher.Find(block, blockStart).ToList();
        Assert.Equal(["ItemText", "CharacterNames"], found.Select(f => f.Table.Name));
        var table = found[0];
        Assert.Equal(blockStart + 0x100, table.Address);

        var edits = new TableEdits();
        edits.SetText("ItemText", 0xFD, "Name", "Phoenix Up (a much longer name)");
        var (text, strings) = TextPatcher.NewText(table, edits);
        // Put the new text right after the table, as TIC Loader would.
        var textAddress = blockStart + block.Length;
        var memory = block.Concat(text).ToArray();
        foreach (var (address, value) in TextPatcher.Pointers(table, strings, textAddress))
            BitConverter.GetBytes(value).CopyTo(memory, address - blockStart);

        var after = NxdTable.Parse(memory[0x100..]);
        Assert.Equal("Phoenix Up (a much longer name)", after.Text(TextTables.Items, 0xFD, TextTables.Items.Field("Name")));
        Assert.Equal("Name 254", after.Text(TextTables.Items, 0xFE, TextTables.Items.Field("Name"))); // others untouched

        Assert.Throws<InvalidOperationException>(() => TextPatcher.Pointers(table, strings, blockStart - 0x1000)); // text before the table can't be reached
    }

    [Fact]
    public void ProjectTextFallsBackToTheGameAndDropsUnchangedEdits()
    {
        var game = new GameText
        {
            Language = "en",
            Tables = new() { ["ItemText"] = new() { [0xFD] = new() { ["Name"] = "Phoenix Down" } } },
        };
        var project = new ModProject(new VanillaData { BuildName = "test", TimeDateStamp = 0, SizeOfImage = 0, Tables = [] }) { GameText = game };
        Assert.Equal("Phoenix Down", project.CurrentText(TextTables.Items, 0xFD, "Name"));

        project.SetText(TextTables.Items, 0xFD, "Name", "Phoenix Up");
        Assert.True(project.IsTextChanged(TextTables.Items, 0xFD, "Name"));
        Assert.Equal("Phoenix Up", project.CurrentText(TextTables.Items, 0xFD, "Name"));

        project.SetText(TextTables.Items, 0xFD, "Name", "Phoenix Down");
        Assert.False(project.IsTextChanged(TextTables.Items, 0xFD, "Name"));
        Assert.True(project.Edits.IsEmpty);
    }

    [Fact]
    public void TextEditsRoundTripThroughTablesJson()
    {
        var edits = TableEdits.Parse("""
            { "Jobs": { "0x01": { "Move": 6 } },
              "Text": { "ItemText": { "0xFD": { "Name": "Phoenix Up", "Description": "Brings \"them\" back." } },
                        "JobText": { "0x01": { "Name": "Hero" } } } }
            """);
        Assert.Equal("Phoenix Up", edits.GetText("ItemText", 0xFD, "Name"));
        Assert.Equal(4, edits.Count);
        var again = TableEdits.Parse(edits.ToJson());
        Assert.Equal(edits.AllText.ToHashSet(), again.AllText.ToHashSet());
        Assert.Equal("6", again.Get("Jobs", 1, "Move"));

        var later = TableEdits.Parse("""{ "Text": { "ItemText": { "0xFD": { "Name": "Phoenix Sideways" } } } }""");
        edits.Merge(later);
        Assert.Equal("Phoenix Sideways", edits.GetText("ItemText", 0xFD, "Name"));
        Assert.Equal("Brings \"them\" back.", edits.GetText("ItemText", 0xFD, "Description"));

        Assert.Throws<FormatException>(() => TableEdits.Parse("""{ "Text": { "Spells": { "0x01": { "Name": "x" } } } }"""));
        Assert.Throws<FormatException>(() => TableEdits.Parse("""{ "Text": { "ItemText": { "0x01": { "Colour": "x" } } } }"""));
        Assert.Throws<FormatException>(() => TableEdits.Parse("""{ "Text": { "ItemText": { "0x01": { "Name": 5 } } } }"""));
    }
}
