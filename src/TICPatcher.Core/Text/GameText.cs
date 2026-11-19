using System.Runtime.Versioning;
using System.Text.Json;
using TICPatcher.Core.Archives;

namespace TICPatcher.Core.Text;

/// <summary>
/// The game's own text for the mapped tables (item, job, ability and character
/// names and descriptions), read from the installed game's archives, in one
/// language. Cached in local app data so later starts are quick.
/// </summary>
public sealed class GameText
{
    public required string Language { get; init; }

    /// <summary>table → id → field → text.</summary>
    public required Dictionary<string, Dictionary<int, Dictionary<string, string>>> Tables { get; init; }

    public string? Get(TextTable table, int id, string field) =>
        Tables.TryGetValue(table.Name, out var rows) && rows.TryGetValue(id, out var fields) && fields.TryGetValue(field, out var text) ? text : null;

    public static string CachePath(string language) => Path.Combine(Mods.VanillaData.Folder, $"text-{language}.json");

    /// <summary>Reads the text from the installed game (the "enhanced" data, language archive 0004).</summary>
    [SupportedOSPlatform("windows")]
    public static GameText FromGame(string gameExe, string language = "en")
    {
        var gameFolder = Path.GetDirectoryName(Path.GetFullPath(gameExe))!;
        var archive = PacArchive.Open(Path.Combine(gameFolder, "data", "enhanced", $"0004.{language}.pac"));
        using var codec = new GDeflate(gameFolder);
        var tables = new Dictionary<string, Dictionary<int, Dictionary<string, string>>>();
        foreach (var table in TextTables.List)
        {
            if (archive.Find(table.File) is not { } entry)
                continue;
            tables[table.Name] = NxdTable.Parse(archive.Read(entry, codec)).AllText(table);
        }
        return new GameText { Language = language, Tables = tables };
    }

    public void Save(string? path = null)
    {
        path ??= CachePath(Language);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static GameText? Load(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) ? JsonSerializer.Deserialize<GameText>(File.ReadAllText(path)) : null;
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            return null;
        }
    }
}
