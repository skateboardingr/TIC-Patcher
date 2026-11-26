namespace TICPatcher.Core.Tables.Schemas;

/// <summary>
/// Errands (FFTPatcher's "Propositions"): the 96 jobs taverns offer. The
/// extra tables FFTPatcher keeps after the records (prices, class and
/// Brave/Faith bonuses, reward amounts) aren't editable yet.
///
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by
/// Joe Davidson and Glain: Datatypes/Propositions/Proposition.cs,
/// AllPropositions.cs. Field names follow FFTPatcher's.
/// </summary>
public static class Errands
{
    public static readonly TableSchema Schema = new(
        "Errands",
        RecordSize: 23,
        RecordCount: 96,
        Fields:
        [
            FieldDef.U8("Type", 0) with { NameList = Names.ErrandTypes },
            FieldDef.U8("BraveFaith", 1) with { NameList = Names.ErrandStats },
            FieldDef.U8("Id1", 2),
            FieldDef.U8("Id2", 3),
            FieldDef.U8("PriceIndex1", 4),
            FieldDef.U8("PriceIndex2", 5),
            FieldDef.U8("MinDays", 6),
            FieldDef.U8("MaxDays", 7),
            FieldDef.U8("Id3", 8),
            FieldDef.U8("BaseSmallReward", 9),
            FieldDef.U8("Reward", 10) with { NameList = Names.ErrandRewards },
            FieldDef.U8("EligibleForBonusCash", 11),
            FieldDef.U8("BaseLargeReward", 12),
            FieldDef.U8("Id4", 13),
            FieldDef.U8("Id5", 14),
            FieldDef.U8("Unknown15", 15),
            FieldDef.U8("Town", 16) with { NameList = Names.Towns },
            FieldDef.U8("PrereqType", 17) with { NameList = Names.ErrandPrereqs },
            FieldDef.U8("Id6", 18),
            FieldDef.U8("Unknown19", 19),
            FieldDef.U8("RandomSuccessClass", 20) with { NameList = Names.ErrandClasses },
            FieldDef.U8("WhenUnlocked", 21) with { NameList = Names.ShopAvailability },
            FieldDef.U8("Prereq", 22),
        ],
        Source: "FFTPatcher Datatypes/Propositions/Proposition.cs") { RecordNames = Names.Errands };

    /// <summary>
    /// Who can go on errands: a unit whose sprite set is at least this (0x4A in the game, so
    /// Ramza, 0x01-0x03, and the story characters, 0x04-0x49, stay home). 0x04 lets the story
    /// characters go but not Ramza; 0x01 lets everyone go. It's the constant of the check at
    /// +0x2EE3B1 (cmp edx, 4Ah) in the function the errand screen asks about each unit
    /// (+0x2EE37C); the other conditions stay: not already on an errand, not a monster, and not
    /// sprite set 0xA2 or 0xA3. That function only counts eligible units; the list itself is
    /// filtered by copies of the same test in the unit-list code (+0x28510B, +0x2C0F3F, +0x2C221B),
    /// which TIC Loader changes too. Like FFTPatcher's PSX patch "Special characters can do
    /// propositions" (Razele), which changes the same kind of constant in two places.
    /// </summary>
    public static readonly TableSchema Eligibility = new(
        "ErrandEligibility",
        RecordSize: 1,
        RecordCount: 1,
        Fields: [FieldDef.U8("LowestSpriteSet", 0) with { Label = "Lowest sprite set that can go (0x4A: generic units only; 0x04: story characters too; 0x01: Ramza too)" }],
        Source: "TIC's exe: the errand eligibility check at +0x2EE37C, its constant at +0x2EE3B3")
    {
        RecordName = _ => "Who can go on errands",
    };
}
