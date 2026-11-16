using TICLoader.Interfaces;
using TICPatcher.Core.Tables;

namespace TICPatcher.Core.Mods;

/// <summary>
/// Gathers every enabled mod's edits, in load order, from all sources: the
/// mod's TIC/tables.json, any registered <see cref="ITICModFormat"/> (e.g. a
/// bridge for another loader's mods), and edits added from code. Later mods
/// win field by field. A broken mod is reported and skipped; the rest still load.
/// </summary>
public sealed class ModCollector
{
    public const string TablesFile = "TIC/tables.json";

    private readonly List<ITICModFormat> _formats = [];
    private readonly Dictionary<string, TableEdits> _fromCode = new(StringComparer.OrdinalIgnoreCase);

    public void AddFormat(ITICModFormat format) => _formats.Add(format);

    public void AddEdits(string sourceModId, IEnumerable<TICFieldEdit> edits)
    {
        var parsed = new TableEdits();
        foreach (var edit in edits)
            parsed.Set(edit.Table, edit.Id, edit.Field, edit.Value);
        if (_fromCode.TryGetValue(sourceModId, out var existing))
            existing.Merge(parsed);
        else
            _fromCode[sourceModId] = parsed;
    }

    /// <summary>Merges the mods' edits; <paramref name="mods"/> is (id, folder) in load order.</summary>
    public TableEdits Collect(IEnumerable<(string Id, string Folder)> mods, Action<string> log)
    {
        var merged = new TableEdits();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, folder) in mods)
        {
            seen.Add(id);
            Add(id, $"{TablesFile}", () => ReadTablesJson(folder), merged, log);
            foreach (var format in _formats)
                Add(id, format.Name, () => FromFormat(format, id, folder), merged, log);
            if (_fromCode.TryGetValue(id, out var code))
                Add(id, "code", () => code, merged, log);
        }
        // Edits added for mods that aren't in the list go last.
        foreach (var (id, code) in _fromCode.Where(c => !seen.Contains(c.Key)))
            Add(id, "code", () => code, merged, log);
        return merged;
    }

    private static void Add(string id, string source, Func<TableEdits?> read, TableEdits merged, Action<string> log)
    {
        try
        {
            if (read() is not { IsEmpty: false } edits)
                return;
            merged.Merge(edits);
            log($"{id}: {edits.All.Count()} change(s) from {source}");
        }
        catch (Exception error) when (error is FormatException or IOException or System.Text.Json.JsonException)
        {
            log($"{id}: skipped {source}, {error.Message}");
        }
    }

    private static TableEdits? ReadTablesJson(string folder)
    {
        var path = Path.Combine(folder, TablesFile);
        return File.Exists(path) ? TableEdits.Parse(File.ReadAllText(path)) : null;
    }

    private static TableEdits? FromFormat(ITICModFormat format, string id, string folder)
    {
        if (format.Read(id, folder) is not { } edits)
            return null;
        var parsed = new TableEdits();
        foreach (var edit in edits)
            parsed.Set(edit.Table, edit.Id, edit.Field, edit.Value);
        return parsed;
    }

    /// <summary>The tables TIC Loader can edit, as the public API describes them.</summary>
    public static IReadOnlyList<TICTableInfo> DescribeTables() =>
        TableEdits.KnownTables.Values.Select(t => new TICTableInfo(t.Name, t.FirstId, t.RecordCount, t.RecordSize,
            t.Fields.Select(f => new TICFieldInfo(f.Name, f.Offset, f.Size, f.Kind.ToString(), f.FlagNames)).ToList())).ToList();
}
