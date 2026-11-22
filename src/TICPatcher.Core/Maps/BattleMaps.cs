using System.Text.Json;
using System.Text.RegularExpressions;
using TICPatcher.Core.Text;

namespace TICPatcher.Core.Maps;

/// <summary>
/// Which map a battle is fought on. The game's own link hasn't been found yet, so the
/// map is guessed from the place in the battle's name ("Zeklaus Desert North 1" →
/// the map named Zeklaus Desert), and the user can pick another; their choices are
/// kept on this PC. Checked against the battles' units: with these guesses, units fit
/// their maps with X as the column and Y as the row.
/// </summary>
public static partial class BattleMaps
{
    /// <summary>The map whose name is the place in <paramref name="battleName"/>, if exactly one is.</summary>
    public static int? Guess(string? battleName, GameText text, IEnumerable<int> maps)
    {
        if (string.IsNullOrWhiteSpace(battleName))
            return null;
        var place = Normal(Suffix().Replace(Chapter().Replace(battleName, ""), ""));
        if (place.Length == 0)
            return null;
        var named = maps.Select(m => (Map: m, Name: Normal(text.MapName(m) ?? ""))).Where(m => m.Name.Length > 0).ToList();
        var exact = named.Where(m => m.Name == place).Select(m => m.Map).ToList();
        if (exact.Count > 0)
            return exact[0];
        var partial = named.Where(m => place.Contains(m.Name) || m.Name.Contains(place)).Select(m => m.Map).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private static string Normal(string name) => Letters().Replace(name.ToLowerInvariant(), "");

    [GeneratedRegex(@"^Chapter \d+ - ")]
    private static partial Regex Chapter();

    [GeneratedRegex(@"\s+((North|South|East|West)\s*)?\d*$")]
    private static partial Regex Suffix();

    [GeneratedRegex("[^a-z]")]
    private static partial Regex Letters();

    /// <summary>The user's choices (battle → map), kept on this PC.</summary>
    public static string ChoicesPath => Path.Combine(Mods.VanillaData.Folder, "battle-maps.json");

    public static Dictionary<int, int> LoadChoices()
    {
        try
        {
            return File.Exists(ChoicesPath) ? JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(ChoicesPath)) ?? [] : [];
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            return [];
        }
    }

    public static void SaveChoices(Dictionary<int, int> choices)
    {
        Directory.CreateDirectory(Mods.VanillaData.Folder);
        File.WriteAllText(ChoicesPath + ".tmp", JsonSerializer.Serialize(choices));
        File.Move(ChoicesPath + ".tmp", ChoicesPath, overwrite: true);
    }
}
