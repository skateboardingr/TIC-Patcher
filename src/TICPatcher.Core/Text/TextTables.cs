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
    ], Tables.Names.SpecialNames) { Rows = 1024, RowLength = 24 }; // row N is a battle unit's special name N (0x07 Argath)

    // Row N is skill set N (0x05 Fundaments, 0x0A White Magicks).
    public static readonly TextTable SkillSets = new("SkillSetText", "jobcommand.nxd", 4,
    [
        new("Name", 4),
        new("Description", 8),
    ], Tables.Names.SkillSets) { Rows = 227, RowLength = 36 };

    // Row N is map surface type N (3 Grassland, 14 Canal, 21 Flagstone; see Maps.MapTerrain).
    public static readonly TextTable Terrain = new("TerrainText", "land.nxd", 4,
    [
        new("Name", 4),
        new("Description", 8),
    ], "Terrain") { Rows = 64, RowLength = 20 };

    // Place names; map N's name is row 10100 + 100 * N (map.nxd), e.g. map 1 "Eagrose Castle Gate".
    public static readonly TextTable Places = new("PlaceNames", "placename.nxd", 8,
    [
        new("Name", 8),
    ], "Places") { Rows = 257, RowLength = 20 };

    // Row N is errand N (the Errands table's record): its title, the tavern's description, and
    // the report written when it ends, in several outcomes ("I ..." or "We ..." as units go alone or not).
    public static readonly TextTable Errands = new("ErrandText", "profit.nxd", 0,
    [
        new("Name", 0),
        new("Description", 4),
        new("Report1", 8),
        new("Report2", 12),
        new("ReportTitle", 16),
        new("Report3", 20),
        new("Report4", 24),
    ], Tables.Names.Errands) { Rows = 96, RowLength = 40 };

    public static readonly IReadOnlyList<TextTable> List = [Items, Jobs, Abilities, Characters, SkillSets, Terrain, Places, Errands];

    /// <summary>The place-name row for a battle map.</summary>
    public static int PlaceOfMap(int map) => 10100 + 100 * map;

    public static readonly IReadOnlyDictionary<string, TextTable> ByName = List.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>The text table for a stat table's records, by their shared name list (Items, Jobs, Abilities).</summary>
    public static TextTable? ForNames(string? names) => List.FirstOrDefault(t => t.Names == names);
}
