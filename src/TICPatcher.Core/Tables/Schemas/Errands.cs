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
}
