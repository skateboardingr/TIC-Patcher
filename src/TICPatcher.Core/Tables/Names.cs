using System.Text.Json;

namespace TICPatcher.Core.Tables;

/// <summary>
/// Display names for jobs, skill sets, statuses, abilities and items, from
/// FFTPatcher's War of the Lions lists (see Resources/names.json for credit).
/// The Ivalice Chronicles renamed a few things; reading names from the game
/// itself comes later.
/// </summary>
public static class Names
{
    public const string Jobs = "Jobs";
    public const string SkillSets = "SkillSets";
    public const string Statuses = "Statuses";
    public const string Abilities = "Abilities";
    public const string Items = "Items";

    private static readonly Lazy<Dictionary<string, Dictionary<int, string>>> Lists = new(Load);

    public static string? Get(string list, int id) =>
        Lists.Value.TryGetValue(list, out var names) && names.TryGetValue(id, out var name) && name.Length > 0 ? name : null;

    private static Dictionary<string, Dictionary<int, string>> Load()
    {
        using var stream = typeof(Names).Assembly.GetManifestResourceStream("names.json")
            ?? throw new InvalidOperationException("names.json resource missing");
        using var document = JsonDocument.Parse(stream);
        var lists = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
        foreach (var list in document.RootElement.EnumerateObject())
        {
            if (list.Value.ValueKind != JsonValueKind.Object)
                continue; // "_source"
            lists[list.Name] = list.Value.EnumerateObject().ToDictionary(e => int.Parse(e.Name), e => e.Value.GetString() ?? "");
        }
        return lists;
    }
}
