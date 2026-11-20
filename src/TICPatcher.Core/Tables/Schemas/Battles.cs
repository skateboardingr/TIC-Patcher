namespace TICPatcher.Core.Tables.Schemas;

/// <summary>
/// Battles (ENTD): who fights in each of the 512 battles. Each battle has 16
/// unit slots of 40 bytes; record N is unit N % 16 of battle N / 16. TIC keeps
/// them in data\enhanced\0002.pac as battle_entd1_ent.bin to battle_entd4_ent.bin
/// (128 battles each), byte for byte the PSX game's ENTD files.
///
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by Joe
/// Davidson and Glain: Datatypes/ENTD/EventUnit.cs, Event.cs, ENTD.cs. Field names
/// follow FFTPatcher's. Several fields have special values (e.g. 0xFE "random",
/// 0xFF "none" for items), as in FFTPatcher.
/// </summary>
public static class Battles
{
    public const int UnitsPerBattle = 16;
    public const int Count = 512;

    /// <summary>The archive files holding the battles, in order (128 battles each).</summary>
    public static readonly IReadOnlyList<string> Files =
        ["battle_entd1_ent.bin", "battle_entd2_ent.bin", "battle_entd3_ent.bin", "battle_entd4_ent.bin"];

    // Special values, as FFTPatcher has them (Datatypes/Items/Item.cs, Abilities/AllAbilities.cs, Job/SkillSet.cs).
    private static readonly Dictionary<int, string> ItemSpecials = new() { [0xFE] = "<Random>", [0xFF] = "<None>" };
    private static readonly Dictionary<int, string> AbilitySpecials = new() { [0x1FE] = "<Random>", [0x1FF] = "<None>" };
    private static readonly Dictionary<int, string> SkillSetSpecials = new() { [0xFE] = "<Random>", [0xFF] = "<Job's own>" };

    public static readonly TableSchema Units = new(
        "Battles",
        RecordSize: 40,
        RecordCount: Count * UnitsPerBattle,
        Fields:
        [
            FieldDef.U8("SpriteSet", 0) with { NameList = Names.SpriteSets },
            FieldDef.Flags("Flags1", 1, ["Male", "Female", "Monster", "JoinAfterEvent", "LoadFormation", "ZodiacMonster", "Blank2", "SaveFormation"]),
            FieldDef.U8("SpecialName", 2) with { NameList = Names.SpecialNames, Specials = new Dictionary<int, string> { [0xFF] = "<Random>" } },
            FieldDef.U8("Level", 3),
            FieldDef.U8("Month", 4) with { NameList = Names.Months },
            FieldDef.U8("Day", 5),
            FieldDef.U8("Bravery", 6),
            FieldDef.U8("Faith", 7),
            FieldDef.U8("PrerequisiteJob", 8) with { NameList = Names.PrerequisiteJobs },
            FieldDef.U8("PrerequisiteJobLevel", 9),
            FieldDef.U8("Job", 10) with { NameList = Names.Jobs, LinksTo = "Jobs" },
            FieldDef.U8("SecondaryAction", 11) with { NameList = Names.SkillSets, LinksTo = "SkillSets", Specials = SkillSetSpecials },
            FieldDef.U16("Reaction", 12) with { NameList = Names.Abilities, LinksTo = "Abilities", Specials = AbilitySpecials },
            FieldDef.U16("Support", 14) with { NameList = Names.Abilities, LinksTo = "Abilities", Specials = AbilitySpecials },
            FieldDef.U16("Movement", 16) with { NameList = Names.Abilities, LinksTo = "Abilities", Specials = AbilitySpecials },
            FieldDef.U8("Head", 18) with { NameList = Names.Items, LinksTo = "Items", Specials = ItemSpecials },
            FieldDef.U8("Body", 19) with { NameList = Names.Items, LinksTo = "Items", Specials = ItemSpecials },
            FieldDef.U8("Accessory", 20) with { NameList = Names.Items, LinksTo = "Items", Specials = ItemSpecials },
            FieldDef.U8("RightHand", 21) with { NameList = Names.Items, LinksTo = "Items", Specials = ItemSpecials },
            FieldDef.U8("LeftHand", 22) with { NameList = Names.Items, LinksTo = "Items", Specials = ItemSpecials },
            FieldDef.U8("Palette", 23),
            FieldDef.Flags("Flags2", 24, ["AlwaysPresent", "RandomlyPresent", null, null, "Control", "Immortal", "Blank6", "Blank7"]),
            FieldDef.Bits("TeamColor", 24, 0x30) with { NameList = Names.TeamColors },
            FieldDef.U8("X", 25),
            FieldDef.U8("Y", 26),
            FieldDef.Bits("FacingDirection", 27, 0x7F) with { NameList = Names.Facing },
            FieldDef.Flags("Elevation", 27, ["UpperLevel"]),
            FieldDef.U8("Experience", 28),
            FieldDef.U8("SkillSet", 29) with { NameList = Names.SkillSets, LinksTo = "SkillSets", Specials = SkillSetSpecials },
            FieldDef.U8("WarTrophy", 30) with { NameList = Names.Items, LinksTo = "Items", Specials = ItemSpecials },
            FieldDef.U8("BonusMoney", 31),
            FieldDef.U8("UnitID", 32),
            FieldDef.U8("TargetX", 33),
            FieldDef.U8("TargetY", 34),
            FieldDef.Flags("AIFlags1", 35, ["Blank8", "FocusUnit", "StayNearXY", "Aggressive", "Defensive", "Blank9", "Blank10", "Blank11"]),
            FieldDef.U8("Target", 36),
            FieldDef.U8("Unknown10", 37),
            FieldDef.Flags("AIFlags2", 38, ["Blank12", "Blank13", "Blank14", "Blank15", "Blank16", "SaveCT", "Blank17", "Blank18"]),
            FieldDef.U8("Unknown12", 39),
        ],
        Source: "FFTPatcher Datatypes/ENTD/EventUnit.cs")
    {
        RecordName = id => $"{Names.Get(Names.Battles, id / UnitsPerBattle) ?? $"Battle {id / UnitsPerBattle:X3}"}, unit {id % UnitsPerBattle + 1}",
    };
}
