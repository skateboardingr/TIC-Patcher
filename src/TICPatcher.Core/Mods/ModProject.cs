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

    /// <summary>Drops every text change to one row.</summary>
    public void RevertText(Text.TextTable table, int id)
    {
        foreach (var (_, _, field, _) in Edits.AllText.Where(e => e.Table == table.Name && e.Record == id).ToList())
            Edits.RemoveText(table.Name, id, field);
        Changed?.Invoke();
    }

    // The text table for a stat table's main records (items, jobs, abilities), if any.
    private static Text.TextTable? TextFor(TableSchema schema) =>
        TextSchemas.Contains(schema.Name) ? Text.TextTables.ForNames(schema.RecordNames) : null;

    private static readonly HashSet<string> TextSchemas =
    [
        Tables.Schemas.Items.Common.Name, Tables.Schemas.Items.Extra.Name, Tables.Schemas.Jobs.Schema.Name, Tables.Schemas.Abilities.Common.Name,
        Tables.Schemas.SkillSets.Main.Name, Tables.Schemas.SkillSets.Extra.Name,
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
        if (schema == Tables.Schemas.Jobs.JobTree)
            SyncJobRequirements();
        Changed?.Invoke();
    }

    /// <summary>The table's bytes as the mod has them.</summary>
    public byte[] CurrentTable(TableSchema schema)
    {
        var bytes = (byte[])Vanilla.Tables[schema.Name].Clone();
        Edits.ApplyTo(schema.Name, bytes, schema.RecordSize);
        return bytes;
    }

    // JobRequirements (what unlocks jobs) follows the job tree: each job needs its prerequisites'
    // requirements too, as the game shipped it. Record N is generic job N + 1 (Chemist first).
    private void SyncJobRequirements()
    {
        if (!HasTable(Tables.Schemas.Jobs.JobTree.Name) || !HasTable(Tables.Schemas.SkillSets.JobRequirements.Name))
            return;
        var requirements = Tables.Schemas.SkillSets.JobRequirements;
        var levels = JobTreeFile.Cumulative(JobTreeFile.Prerequisites(CurrentTable(Tables.Schemas.Jobs.JobTree)));
        for (var job = 1; job < levels.Length && job - 1 < requirements.RecordCount; job++)
            for (var other = 0; other < requirements.Fields.Count && other < levels[job].Length; other++)
            {
                var field = requirements.Fields[other].Name;
                var wanted = Math.Min(levels[job][other], 15).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (Current(requirements, job - 1, field) == wanted)
                    continue;
                if (wanted == Original(requirements, job - 1, field))
                    Edits.Remove(requirements.Name, job - 1, field);
                else
                    Edits.Set(requirements.Name, job - 1, field, wanted);
            }
    }

    /// <summary>
    /// Files the mod hands the game in place of its own (path in the mod → contents): the job tree
    /// when its prerequisites change.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> ModFiles()
    {
        var files = new Dictionary<string, byte[]>();
        if (Edits.ChangedTables.Contains(Tables.Schemas.Jobs.JobTree.Name)
            && Vanilla.Files.TryGetValue(JobTreeFile.VanillaKey, out var original))
            files[JobTreeFile.ModPath] = JobTreeFile.Build(original, CurrentTable(Tables.Schemas.Jobs.JobTree));
        return files;
    }

    public void Revert(TableSchema schema, int id)
    {
        // All of the record's edits, including raw bytes ("+N") from imported mods.
        foreach (var (_, _, field, _) in Edits.All.Where(e => e.Table == schema.Name && e.Record == id).ToList())
            Edits.Remove(schema.Name, id, field);
        if (TextFor(schema) is { } text)
            foreach (var (_, _, field, _) in Edits.AllText.Where(e => e.Table == text.Name && e.Record == id).ToList())
                Edits.RemoveText(text.Name, id, field);
        if (schema == Tables.Schemas.Jobs.JobTree)
            SyncJobRequirements();
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
