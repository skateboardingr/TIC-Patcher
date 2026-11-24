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

    public static IReadOnlyDictionary<string, TableSchema> KnownTables => Tables.Schemas.AllTables.ByName;

    // Text edits, kept apart from the stat tables: text table -> id -> field -> text.
    private readonly Dictionary<string, SortedDictionary<int, Dictionary<string, string>>> _text = new(StringComparer.Ordinal);

    /// <summary>The section of tables.json holding text edits (names, descriptions).</summary>
    public const string TextSection = "Text";

    public bool IsEmpty => _edits.Count == 0 && _text.Count == 0;

    private readonly List<string> _unknownTables = [];

    /// <summary>Tables in the parsed tables.json that this version doesn't know (left out).</summary>
    public IReadOnlyList<string> UnknownTables => _unknownTables;

    /// <summary>Every change, stats and text.</summary>
    public int Count => All.Count() + AllText.Count();

    public IEnumerable<(string Table, int Record, string Field, string Value)> AllText =>
        from table in _text
        from record in table.Value
        from field in record.Value
        select (table.Key, record.Key, field.Key, field.Value);

    public void SetText(string table, int record, string field, string value)
    {
        var text = Text.TextTables.ByName.TryGetValue(table, out var t) ? t : throw new FormatException($"Unknown text table \"{table}\"");
        if (!text.Fields.Any(f => f.Name == field))
            throw new FormatException($"{table}: unknown text field \"{field}\"");
        if (record < 0)
            throw new FormatException($"{table}: record {record} isn't valid");
        if (value.Length > 2000)
            throw new FormatException($"{table} 0x{record:X2} {field}: text is too long");
        if (!_text.TryGetValue(table, out var records))
            _text[table] = records = new SortedDictionary<int, Dictionary<string, string>>();
        if (!records.TryGetValue(record, out var fields))
            records[record] = fields = new Dictionary<string, string>(StringComparer.Ordinal);
        fields[field] = value;
    }

    public string? GetText(string table, int record, string field) =>
        _text.TryGetValue(table, out var records) && records.TryGetValue(record, out var fields) && fields.TryGetValue(field, out var value)
            ? value
            : null;

    public bool RemoveText(string table, int record, string field)
    {
        if (!_text.TryGetValue(table, out var records) || !records.TryGetValue(record, out var fields) || !fields.Remove(field))
            return false;
        if (fields.Count == 0)
            records.Remove(record);
        if (records.Count == 0)
            _text.Remove(table);
        return true;
    }

    public IEnumerable<(string Table, int Record, string Field, string Value)> All =>
        from table in _edits
        from record in table.Value
        from field in record.Value
        select (table.Key, record.Key, field.Key, field.Value);

    public void Set(string table, int record, string field, string value)
    {
        var schema = KnownTables.TryGetValue(table, out var s) ? s : throw new FormatException($"Unknown table \"{table}\"");
        if (record < schema.FirstId || record >= schema.FirstId + schema.RecordCount)
            throw new FormatException($"{table}: record 0x{record:X2} is outside 0x{schema.FirstId:X2}-0x{schema.FirstId + schema.RecordCount - 1:X2}");
        var def = FieldFor(schema, field) ?? throw new FormatException($"{table}: unknown field \"{field}\"");
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

    /// <summary>
    /// A field by name, or "+N" for the raw byte at offset N of the record, for
    /// bytes that have no name yet (e.g. when a bridge maps another format by offset).
    /// </summary>
    public static FieldDef? FieldFor(TableSchema schema, string field)
    {
        if (field.StartsWith('+'))
            return int.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset < schema.RecordSize
                ? FieldDef.U8(field, offset)
                : null;
        return schema.Fields.FirstOrDefault(f => f.Name == field);
    }

    public bool Remove(string table, int record, string field)
    {
        if (!_edits.TryGetValue(table, out var records) || !records.TryGetValue(record, out var fields) || !fields.Remove(field))
            return false;
        if (fields.Count == 0)
            records.Remove(record);
        if (records.Count == 0)
            _edits.Remove(table);
        return true;
    }

    public string? Get(string table, int record, string field) =>
        _edits.TryGetValue(table, out var records) && records.TryGetValue(record, out var fields) && fields.TryGetValue(field, out var value)
            ? value
            : null;

    /// <summary>
    /// The edits as tables.json: record ids in hex, numbers as numbers and
    /// flags as strings, tables and fields in schema order.
    /// </summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            foreach (var schema in KnownTables.Values.Where(s => _edits.ContainsKey(s.Name)))
            {
                json.WriteStartObject(schema.Name);
                foreach (var (record, fields) in _edits[schema.Name])
                {
                    json.WriteStartObject($"0x{record:X2}");
                    var raw = fields.Keys.Where(f => f.StartsWith('+')).OrderBy(f => int.Parse(f.AsSpan(1), CultureInfo.InvariantCulture));
                    foreach (var def in schema.Fields.Where(f => fields.ContainsKey(f.Name)).Concat(raw.Select(f => FieldFor(schema, f)!)))
                    {
                        var value = fields[def.Name];
                        if (def.Kind == FieldKind.Flags)
                            json.WriteString(def.Name, value);
                        else
                            json.WriteNumber(def.Name, int.Parse(value, CultureInfo.InvariantCulture));
                    }
                    json.WriteEndObject();
                }
                json.WriteEndObject();
            }
            if (_text.Count > 0)
            {
                json.WriteStartObject(TextSection);
                foreach (var table in Text.TextTables.List.Where(t => _text.ContainsKey(t.Name)))
                {
                    json.WriteStartObject(table.Name);
                    foreach (var (record, fields) in _text[table.Name])
                    {
                        json.WriteStartObject($"0x{record:X2}");
                        foreach (var field in table.Fields.Where(f => fields.ContainsKey(f.Name)))
                            json.WriteString(field.Name, fields[field.Name]);
                        json.WriteEndObject();
                    }
                    json.WriteEndObject();
                }
                json.WriteEndObject();
            }
            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    /// <summary>Adds another mod's edits on top of these (they win where both set a field).</summary>
    public void Merge(TableEdits later)
    {
        foreach (var (table, record, field, value) in later.All)
            Set(table, record, field, value);
        foreach (var (table, record, field, value) in later.AllText)
            SetText(table, record, field, value);
    }

    /// <summary>Fields both edits set, to different values (merging gives <paramref name="later"/>'s).</summary>
    public List<string> Conflicts(TableEdits later) =>
    [
        .. later.All.Where(e => Get(e.Table, e.Record, e.Field) is { } mine && mine != e.Value)
            .Select(e => $"{e.Table} 0x{e.Record:X2} {e.Field}"),
        .. later.AllText.Where(e => GetText(e.Table, e.Record, e.Field) is { } mine && mine != e.Value)
            .Select(e => $"{e.Table} 0x{e.Record:X2} {e.Field}"),
    ];

    /// <summary>The tables these edits change.</summary>
    public IEnumerable<string> ChangedTables => _edits.Keys;

    /// <summary>
    /// Applies the edits for one table to its bytes in place. Record ids are
    /// the game's (see <see cref="TableSchema.FirstId"/>).
    /// </summary>
    public int ApplyTo(string table, Span<byte> tableBytes, int recordSize)
    {
        if (!_edits.TryGetValue(table, out var records))
            return 0;
        var schema = KnownTables[table];
        var applied = 0;
        foreach (var (record, fields) in records)
        {
            var bytes = tableBytes.Slice((record - schema.FirstId) * recordSize, schema.RecordSize);
            // Named fields first, then raw bytes ("+N"), so a raw byte can refine a named field.
            foreach (var (field, value) in fields.OrderBy(f => f.Key.StartsWith('+')))
            {
                RecordCodec.Write(FieldFor(schema, field)!, bytes, value);
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
            if (table.Name == TextSection)
            {
                ParseText(table.Value, edits);
                continue;
            }
            // A table from a newer TIC Patcher is left out, not the whole mod.
            if (!KnownTables.ContainsKey(table.Name))
            {
                edits._unknownTables.Add(table.Name);
                continue;
            }
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

    // "Text": { "ItemText": { "0xFD": { "Name": "Phoenix Up" } } }
    private static void ParseText(JsonElement section, TableEdits edits)
    {
        foreach (var table in section.EnumerateObject())
        {
            if (table.Value.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{TextSection}.{table.Name} must be an object of records");
            foreach (var record in table.Value.EnumerateObject())
            {
                var id = ParseId(record.Name) ?? throw new FormatException($"{table.Name}: \"{record.Name}\" isn't a record id");
                foreach (var field in record.Value.EnumerateObject())
                {
                    if (field.Value.ValueKind != JsonValueKind.String)
                        throw new FormatException($"{table.Name} {record.Name} {field.Name}: text must be a string");
                    edits.SetText(table.Name, id, field.Name, field.Value.GetString()!);
                }
            }
        }
    }

    private static int? ParseId(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}
