namespace TICPatcher.Core.Tables.Schemas;

/// <summary>Every table TIC Patcher can edit, by name.</summary>
public static class AllTables
{
    public static readonly IReadOnlyList<TableSchema> List =
    [
        Jobs.Schema,
        Jobs.FormationSprites1, Jobs.FormationSprites2, Jobs.FormationSlotSprites, Jobs.SlotSprites, Jobs.SpriteFormationSlots,
        SkillSets.JobRequirements,
        SkillSets.Main, SkillSets.Extra, SkillSets.MonsterSkills, SkillSets.ActionMenus,
        Abilities.Common, Abilities.Action, Abilities.Throw, Abilities.Jump, Abilities.Charge,
        Abilities.Effects, Abilities.Animations,
        Items.Common, Items.Weapons, Items.Shields, Items.Armor, Items.Accessories, Items.ChemistItems,
        Items.Extra, Items.ExtraWeapons, Items.ExtraArmor,
        Items.Attributes, Items.ShopInventories,
        Statuses.Attributes, Statuses.Inflict,
        Errands.Schema, Errands.Eligibility,
        Battles.Units,
        SpriteSheets.Palettes,
    ];

    public static readonly IReadOnlyDictionary<string, TableSchema> ByName =
        List.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>
    /// The table holding record <paramref name="id"/> for a link to <paramref name="table"/>:
    /// that table if the id is in its range, otherwise another table whose records are
    /// named from the same list (e.g. skill set 0xB0 lives in MonsterSkills).
    /// </summary>
    public static TableSchema? Resolve(string table, int id)
    {
        if (!ByName.TryGetValue(table, out var wanted))
            return null;
        static bool Holds(TableSchema t, int id) => id >= t.FirstId && id < t.FirstId + t.RecordCount;
        return Holds(wanted, id) ? wanted
            : wanted.RecordNames is { } names ? List.FirstOrDefault(t => t.RecordNames == names && Holds(t, id)) : null;
    }

    /// <summary>Other tables with a record for the same thing, e.g. an item's weapon data and shop list.</summary>
    public static IEnumerable<TableSchema> Related(TableSchema table, int id) =>
        table.RecordNames is { } names
            ? List.Where(t => t != table && t.RecordNames == names && id >= t.FirstId && id < t.FirstId + t.RecordCount)
            : [];
}
