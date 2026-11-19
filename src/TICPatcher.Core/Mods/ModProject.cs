using TICPatcher.Core.Tables;

namespace TICPatcher.Core.Mods;

/// <summary>
/// A mod being edited: the original tables plus the mod's changes. Setting a
/// field back to its original value drops the change, so a saved mod only
/// ever holds what differs from the game.
/// </summary>
public sealed class ModProject(VanillaData vanilla, TableEdits? edits = null)
{
    public VanillaData Vanilla { get; } = vanilla;
    public TableEdits Edits { get; } = edits ?? new TableEdits();

    public event Action? Changed;

    /// <summary>The game's own text (names, descriptions), if it could be read.</summary>
    public Text.GameText? GameText { get; set; }

    public string? OriginalText(Text.TextTable table, int id, string field) => GameText?.Get(table, id, field);

    public string? CurrentText(Text.TextTable table, int id, string field) =>
        Edits.GetText(table.Name, id, field) ?? OriginalText(table, id, field);

    public bool IsTextChanged(Text.TextTable table, int id, string field) => Edits.GetText(table.Name, id, field) is not null;

    /// <summary>Sets text; setting it back to the game's own drops the change.</summary>
    public void SetText(Text.TextTable table, int id, string field, string value)
    {
        if (value == OriginalText(table, id, field))
            Edits.RemoveText(table.Name, id, field);
        else
            Edits.SetText(table.Name, id, field, value);
        Changed?.Invoke();
    }

    // The text table for a stat table's main records (items, jobs, abilities), if any.
    private static Text.TextTable? TextFor(TableSchema schema) =>
        TextSchemas.Contains(schema.Name) ? Text.TextTables.ForNames(schema.RecordNames) : null;

    private static readonly HashSet<string> TextSchemas =
    [
        Tables.Schemas.Items.Common.Name, Tables.Schemas.Items.Extra.Name, Tables.Schemas.Jobs.Schema.Name, Tables.Schemas.Abilities.Common.Name,
    ];

    /// <summary>The text fields shown with a stat table's records, if any.</summary>
    public Text.TextTable? TextTableFor(TableSchema schema) => GameText is null ? null : TextFor(schema);

    public bool HasTable(string table) => Vanilla.Tables.ContainsKey(table);

    public byte[] OriginalRecord(TableSchema schema, int id) =>
        Vanilla.Tables[schema.Name].AsSpan((id - schema.FirstId) * schema.RecordSize, schema.RecordSize).ToArray();

    public string Original(TableSchema schema, int id, string field) =>
        RecordCodec.Read(schema.Field(field), OriginalRecord(schema, id));

    public string Current(TableSchema schema, int id, string field) =>
        Edits.Get(schema.Name, id, field) is { } value
            ? Normalize(schema, id, field, value)
            : Original(schema, id, field);

    public bool IsChanged(TableSchema schema, int id, string field) => Edits.Get(schema.Name, id, field) is not null;

    public bool IsChanged(TableSchema schema, int id) =>
        Edits.All.Any(e => e.Table == schema.Name && e.Record == id)
        || (TextFor(schema) is { } text && Edits.AllText.Any(e => e.Table == text.Name && e.Record == id));

    /// <summary>Sets a field; throws FormatException for a value the field can't hold.</summary>
    public void Set(TableSchema schema, int id, string field, string value)
    {
        var normal = Normalize(schema, id, field, value);
        if (normal == Original(schema, id, field))
            Edits.Remove(schema.Name, id, field);
        else
            Edits.Set(schema.Name, id, field, normal);
        Changed?.Invoke();
    }

    public void Revert(TableSchema schema, int id)
    {
        // All of the record's edits, including raw bytes ("+N") from imported mods.
        foreach (var (_, _, field, _) in Edits.All.Where(e => e.Table == schema.Name && e.Record == id).ToList())
            Edits.Remove(schema.Name, id, field);
        if (TextFor(schema) is { } text)
            foreach (var (_, _, field, _) in Edits.AllText.Where(e => e.Table == text.Name && e.Record == id).ToList())
                Edits.RemoveText(text.Name, id, field);
        Changed?.Invoke();
    }

    // The value as the codec reads it back, e.g. flags in bit order.
    private string Normalize(TableSchema schema, int id, string field, string value)
    {
        var def = schema.Field(field);
        var record = OriginalRecord(schema, id);
        try
        {
            RecordCodec.Write(def, record, value);
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"{field}: \"{value}\" isn't valid ({error.Message})", error);
        }
        return RecordCodec.Read(def, record);
    }
}
