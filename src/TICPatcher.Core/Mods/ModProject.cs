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

    public bool IsChanged(TableSchema schema, int id) => schema.Fields.Any(f => IsChanged(schema, id, f.Name));

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
        foreach (var field in schema.Fields)
            Edits.Remove(schema.Name, id, field.Name);
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
