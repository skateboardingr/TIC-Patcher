namespace TICPatcher.Core.Text;

/// <summary>A text field of a .nxd row: its name in mods, and the offset of its string pointer in the row.</summary>
public sealed record TextField(string Name, int Offset);

/// <summary>
/// A .nxd table with text TIC Patcher can edit. <see cref="Base"/> is where the
/// string offsets count from, relative to the row start. <see cref="Names"/>
/// is the name list these ids share with the stat tables (an item's text and
/// its stats have the same id).
/// </summary>
public sealed record TextTable(string Name, string File, int Base, IReadOnlyList<TextField> Fields, string Names)
{
    /// <summary>How many rows the game's table has, and its row length (0 if rows vary): to recognise it in memory.</summary>
    public int Rows { get; init; }
    public int RowLength { get; init; }

    public TextField Field(string name) =>
        Fields.FirstOrDefault(f => f.Name == name) ?? throw new KeyNotFoundException($"{Name} has no text field {name}");
}

/// <summary>
/// The text tables mapped so far (docs/archives.md). Which fields are text was
/// found by comparing the English and French tables; where their offsets count
/// from, by checking every row resolves to a string.
/// </summary>
public static class TextTables
{
    public static readonly TextTable Items = new("ItemText", "item.nxd", 0,
    [
        new("Name", 0),
        new("Singular", 4),     // "tuft of phoenix down", used in sentences
        new("Plural", 8),       // "tufts of phoenix down"
        new("Description", 12),
        new("FullName", 16),    // the name spelled out ("Materia Blade Plus"); otherwise the same as Name
    ], Tables.Names.Items) { Rows = 261, RowLength = 48 };

    public static readonly TextTable Jobs = new("JobText", "job.nxd", 4,
    [
        new("Name", 4),
        new("Description", 12),
    ], Tables.Names.Jobs) { Rows = 174, RowLength = 64 };

    public static readonly TextTable Abilities = new("AbilityText", "ability.nxd", 4,
    [
        new("Name", 4),
        new("Description", 8),
    ], Tables.Names.Abilities) { Rows = 512 };

    public static readonly TextTable Characters = new("CharacterNames", "charaname.nxd", 4,
    [
        new("Name", 4),
    ], "Characters") { Rows = 1024, RowLength = 24 };

    // Row N is skill set N (0x05 Fundaments, 0x0A White Magicks).
    public static readonly TextTable SkillSets = new("SkillSetText", "jobcommand.nxd", 4,
    [
        new("Name", 4),
        new("Description", 8),
    ], Tables.Names.SkillSets) { Rows = 227, RowLength = 36 };

    public static readonly IReadOnlyList<TextTable> List = [Items, Jobs, Abilities, Characters, SkillSets];

    public static readonly IReadOnlyDictionary<string, TextTable> ByName = List.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>The text table for a stat table's records, by their shared name list (Items, Jobs, Abilities).</summary>
    public static TextTable? ForNames(string? names) => List.FirstOrDefault(t => t.Names == names);
}
