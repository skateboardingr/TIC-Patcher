using System.Globalization;
using System.Text.Json;
using TICPatcher.Core.Tables;

namespace TICPatcher.Core.Mods;

/// <summary>
/// A mod's table edits, as stored in a TIC mod's TIC/tables.json:
/// <code>
/// { "Jobs": { "0x01": { "Move": "6", "Equipment": "Knife, Sword" } } }
/// </code>
/// Record ids are hex ("0x01") or decimal; values use the same text as
/// TIC Patcher (numbers, or flag names joined by ", "). Edits from several
/// mods merge in load order, later mods winning field by field.
/// </summary>
public sealed class TableEdits
{
    // table -> record -> field -> value
    private readonly Dictionary<string, SortedDictionary<int, Dictionary<string, string>>> _edits = new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, TableSchema> KnownTables { get; } =
        new Dictionary<string, TableSchema> { [Tables.Schemas.Jobs.Schema.Name] = Tables.Schemas.Jobs.Schema };

    public bool IsEmpty => _edits.Count == 0;

    public IEnumerable<(string Table, int Record, string Field, string Value)> All =>
        from table in _edits
        from record in table.Value
        from field in record.Value
        select (table.Key, record.Key, field.Key, field.Value);

    public void Set(string table, int record, string field, string value)
    {
        var schema = KnownTables.TryGetValue(table, out var s) ? s : throw new FormatException($"Unknown table \"{table}\"");
        if (record < 0 || record >= schema.RecordCount)
            throw new FormatException($"{table}: record 0x{record:X2} is outside 0x00-0x{schema.RecordCount - 1:X2}");
        var def = schema.Fields.FirstOrDefault(f => f.Name == field) ?? throw new FormatException($"{table}: unknown field \"{field}\"");
        // Validate the value now, so a bad mod is reported when it loads, not mid-apply.
        try
        {
            RecordCodec.Write(def, new byte[schema.RecordSize], value);
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"{table} 0x{record:X2} {field}: \"{value}\" isn't valid ({error.Message})");
        }

        if (!_edits.TryGetValue(table, out var records))
            _edits[table] = records = new SortedDictionary<int, Dictionary<string, string>>();
        if (!records.TryGetValue(record, out var fields))
            records[record] = fields = new Dictionary<string, string>(StringComparer.Ordinal);
        fields[field] = value;
    }

    /// <summary>Adds another mod's edits on top of these (they win where both set a field).</summary>
    public void Merge(TableEdits later)
    {
        foreach (var (table, record, field, value) in later.All)
            Set(table, record, field, value);
    }

    /// <summary>Applies the edits for one table to its bytes in place.</summary>
    public int ApplyTo(string table, Span<byte> tableBytes, int recordSize)
    {
        if (!_edits.TryGetValue(table, out var records))
            return 0;
        var schema = KnownTables[table];
        var applied = 0;
        foreach (var (record, fields) in records)
        {
            var bytes = tableBytes.Slice(record * recordSize, schema.RecordSize);
            foreach (var (field, value) in fields)
            {
                RecordCodec.Write(schema.Field(field), bytes, value);
                applied++;
            }
        }
        return applied;
    }

    public static TableEdits Parse(string json)
    {
        var edits = new TableEdits();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("tables.json must be an object of tables");
        foreach (var table in document.RootElement.EnumerateObject())
        {
            if (table.Value.ValueKind != JsonValueKind.Object)
                throw new FormatException($"\"{table.Name}\" must be an object of records");
            foreach (var record in table.Value.EnumerateObject())
            {
                var id = ParseId(record.Name) ?? throw new FormatException($"{table.Name}: \"{record.Name}\" isn't a record id (use e.g. \"0x4A\" or \"74\")");
                foreach (var field in record.Value.EnumerateObject())
                {
                    var value = field.Value.ValueKind switch
                    {
                        JsonValueKind.String => field.Value.GetString()!,
                        JsonValueKind.Number => field.Value.GetRawText(),
                        _ => throw new FormatException($"{table.Name} {record.Name} {field.Name}: use a number or a string"),
                    };
                    edits.Set(table.Name, id, field.Name, value);
                }
            }
        }
        return edits;
    }

    private static int? ParseId(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}
