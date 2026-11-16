namespace TICPatcher.Core.Tables.Schemas;

/// <summary>
/// Status tables: the 40 statuses' rules, and the 128 inflict-status sets
/// that abilities and weapons refer to by number.
///
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by
/// Joe Davidson and Glain: Datatypes/Status/StatusAttribute.cs, InflictStatus.cs,
/// Statuses.cs. Field names follow FFTPatcher's; "Not..." flags are stored inverted.
/// </summary>
public static class Statuses
{
    public static readonly TableSchema Attributes = new(
        "StatusAttributes",
        RecordSize: 16,
        RecordCount: 40,
        Fields:
        [
            FieldDef.U8("Blank1", 0),
            FieldDef.U8("Blank2", 1),
            FieldDef.U8("Order", 2),
            FieldDef.U8("Ticks", 3),
            FieldDef.Flags("Flags1", 4, ["FreezeCT", "Unknown1", "Unknown2", "Unknown3", "Unknown4", "Unknown5", "Unknown6", "CountsAsKO"]),
            FieldDef.Flags("Flags2", 5, ["NotCanReact", "Blank", "IgnoreAttacks", "IgnoredIfMount", "Unknown8", "Unknown9", "CancelledByImmortal", "LowerTargetPriority"]),
            FieldDef.Flags("Cancels", 6, Jobs.Statuses),
            FieldDef.Flags("CantStackOn", 11, Jobs.Statuses),
        ],
        Source: "FFTPatcher Datatypes/Status/StatusAttribute.cs");

    public static readonly TableSchema Inflict = new(
        "InflictStatuses",
        RecordSize: 6,
        RecordCount: 128,
        Fields:
        [
            FieldDef.Flags("Flags", 0, ["AllOrNothing", "Random", "Separate", "Cancel", "Blank1", "Blank2", "Blank3", "Blank4"]),
            FieldDef.Flags("Statuses", 1, Jobs.Statuses),
        ],
        Source: "FFTPatcher Datatypes/Status/InflictStatus.cs");
}
