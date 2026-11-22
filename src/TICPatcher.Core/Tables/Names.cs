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
    public const string Effects = "Effects";
    public const string Formulas = "Formulas";
    public const string ShopAvailability = "ShopAvailability";
    public const string ItemTypes = "ItemTypes";
    public const string MenuActions = "MenuActions";
    public const string AbilityTypes = "AbilityTypes";
    public const string Errands = "Errands";
    public const string ErrandTypes = "ErrandTypes";
    public const string ErrandStats = "ErrandStats";
    public const string ErrandRewards = "ErrandRewards";
    public const string ErrandPrereqs = "ErrandPrereqs";
    public const string ErrandClasses = "ErrandClasses";
    public const string Towns = "Towns";
    public const string Battles = "Battles";
    public const string SpriteSets = "SpriteSets";
    public const string SpecialNames = "SpecialNames";
    public const string Months = "Months";
    public const string PrerequisiteJobs = "PrerequisiteJobs";
    public const string Facing = "Facing";
    public const string TeamColors = "TeamColors";
    public const string SpriteFiles = "SpriteFiles";

    /// <summary>Not a fixed list: the editor names each entry from the mod's current InflictStatuses table.</summary>
    public const string InflictStatuses = "InflictStatuses";

    private static readonly Lazy<Dictionary<string, Dictionary<int, string>>> Lists = new(Load);

    /// <summary>
    /// The Ivalice Chronicles reuses War of the Lions' item ids 0x100-0x104 (the
    /// Fell Swords) for its own five extra items and has no items above 0x104,
    /// so those names come from the game, not FFTPatcher's PSP list.
    /// </summary>
    public const int LastItem = 0x104;

    /// <summary>
    /// Names that take priority over FFTPatcher's lists, e.g. the game's own text
    /// with a mod's renames (set by the editor). Empty or null falls back to the lists.
    /// </summary>
    public static Func<string, int, string?>? Provider { get; set; }

    public static string? Get(string list, int id)
    {
        if (Provider?.Invoke(list, id) is { Length: > 0 } name)
            return name;
        return Fallback(list, id);
    }

    /// <summary>FFTPatcher's (War of the Lions) name, ignoring <see cref="Provider"/>.</summary>
    public static string? Fallback(string list, int id)
    {
        if (list == Items && id >= 0x100)
            return id <= LastItem ? Schemas.Items.ExtraNames.GetValueOrDefault(id) : null;
        return Lists.Value.TryGetValue(list, out var names) && names.TryGetValue(id, out var name) && name.Length > 0 ? name : null;
    }

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
