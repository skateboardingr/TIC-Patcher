namespace TICPatcher.Core.Tables.Schemas;

/// <summary>Every table TIC Patcher can edit, by name.</summary>
public static class AllTables
{
    public static readonly IReadOnlyList<TableSchema> List =
    [
        Jobs.Schema,
        SkillSets.JobRequirements,
        SkillSets.Main, SkillSets.Extra, SkillSets.MonsterSkills, SkillSets.ActionMenus,
        Abilities.Common, Abilities.Action, Abilities.Throw, Abilities.Jump, Abilities.Charge,
        Abilities.Effects, Abilities.Animations,
        Items.Common, Items.Weapons, Items.Shields, Items.Armor, Items.Accessories, Items.ChemistItems,
        Items.Attributes, Items.ShopInventories,
        Statuses.Attributes, Statuses.Inflict,
    ];

    public static readonly IReadOnlyDictionary<string, TableSchema> ByName =
        List.ToDictionary(t => t.Name, StringComparer.Ordinal);
}
