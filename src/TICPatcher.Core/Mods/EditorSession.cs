using System.Text.Json;

namespace TICPatcher.Core.Mods;

/// <summary>
/// What the editor was working on, saved as you edit so nothing is lost when
/// it closes: the mod (its Reloaded-II id once saved), its name, and its
/// changes as tables.json text. Restored at the next start.
/// </summary>
public sealed record EditorSession(string? ModId, string ModName, string TablesJson)
{
    public static string DefaultPath => Path.Combine(VanillaData.Folder, "session.json");

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>The last session, or null if there isn't one or it can't be read.</summary>
    public static EditorSession? Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<EditorSession>(File.ReadAllText(path)) : null;
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            return null; // a damaged session file shouldn't stop the editor starting
        }
    }
}
