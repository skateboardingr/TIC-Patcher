namespace TICPatcher.Core.Tables.Schemas;

/// <summary>
/// Ability tables. Ability ids: 0x000-0x16F action abilities, 0x170-0x17D
/// item, 0x17E-0x189 throw, 0x18A-0x195 jump, 0x196-0x19D charge,
/// 0x19E-0x1A5 arithmetick, 0x1A6-0x1FF reaction/support/movement.
///
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by
/// Joe Davidson and Glain: Datatypes/Abilities/Ability.cs, AbilityAttributes.cs,
/// AIFlags.cs, AllAbilities.cs, AllAnimations.cs. Field names follow FFTPatcher's.
/// A flag named "Not..." is stored inverted: FFTPatcher shows the opposite
/// of the bit (e.g. NotTargetSelf set = "Target self" unticked).
/// The Ivalice Chronicles' action ability records are 20 bytes, not 14:
/// bytes 14-19 are new and not yet understood (see docs/tables.md).
/// </summary>
public static class Abilities
{
    public static readonly TableSchema Common = new(
        "Abilities",
        RecordSize: 8,
        RecordCount: 512,
        Fields:
        [
            FieldDef.U16("JPCost", 0),
            FieldDef.U8("LearnRate", 2),
            FieldDef.Flags("Flags", 3, ["NotLearnWithJP", "DisplayAbilityName", "LearnOnHit", "Blank1", null, null, null, null]),
            FieldDef.Lower("AbilityType", 3),
            FieldDef.Flags("AIFlags", 4,
            [
                "HP", "MP", "CancelStatus", "AddStatus", "Stats", "Unequip", "TargetEnemies", "TargetAllies",
                "LineOfSight", "Reflectable", "UndeadReverse", "Unknown1", "AllowRandomly", "Unknown2", "Unknown3", "NotSilence",
                "Blank", "Unknown4", "Unknown5", "NotUnknown6", "Unknown7", "Unknown8", "Unknown9", "Unknown10",
            ]),
            FieldDef.Flags("Flags2", 7, ["UsedByEnemies", "Unknown2", "Unknown3", "Blank2", "Blank3", "Blank4", "Blank5", "Unknown4"]),
        ],
        Source: "FFTPatcher Datatypes/Abilities/Ability.cs, AIFlags.cs") { RecordNames = Names.Abilities };

    public static readonly TableSchema Action = new(
        "ActionAbilities",
        RecordSize: 20,
        RecordCount: 0x170,
        Fields:
        [
            FieldDef.U8("Range", 0),
            FieldDef.U8("Effect", 1),
            FieldDef.U8("Vertical", 2),
            FieldDef.Flags("Flags1", 3, ["ForceSelfTarget", "Blank7", "WeaponRange", "VerticalFixed", "VerticalTolerance", "WeaponStrike", "Auto", "NotTargetSelf"]),
            FieldDef.Flags("Flags2", 4, ["NotHitEnemies", "NotHitAllies", "TopDownTarget", "NotFollowTarget", "RandomFire", "LinearAttack", "ThreeDirections", "NotHitCaster"]),
            FieldDef.Flags("Flags3", 5, ["Reflect", "Arithmetick", "NotSilence", "NotMimic", "NormalAttack", "Perservere", "ShowQuote", "AnimateMiss"]),
            FieldDef.Flags("Flags4", 6, ["CounterFlood", "CounterMagic", "Direct", "Shirahadori", "RequiresSword", "RequiresMateriaBlade", "Evadeable", "NotTargeting"]),
            FieldDef.Flags("Elements", 7, Jobs.Elements),
            FieldDef.U8("Formula", 8),
            FieldDef.U8("X", 9),
            FieldDef.U8("Y", 10),
            FieldDef.U8("InflictStatus", 11),
            FieldDef.U8("Ticks", 12),
            FieldDef.U8("MPCost", 13),
        ],
        Source: "FFTPatcher Datatypes/Abilities/AbilityAttributes.cs; record size measured in TIC") { RecordNames = Names.Abilities };

    public static readonly TableSchema Throw = new(
        "ThrowAbilities", RecordSize: 1, RecordCount: 12,
        Fields: [FieldDef.U8("ItemType", 0)],
        Source: "FFTPatcher Datatypes/Abilities/Ability.cs (Throwing)") { FirstId = 0x17E, RecordNames = Names.Abilities };

    public static readonly TableSchema Jump = new(
        "JumpAbilities", RecordSize: 2, RecordCount: 12,
        Fields: [FieldDef.U8("JumpHorizontal", 0), FieldDef.U8("JumpVertical", 1)],
        Source: "FFTPatcher Datatypes/Abilities/Ability.cs (JumpHorizontal, JumpVertical)") { FirstId = 0x18A, RecordNames = Names.Abilities };

    public static readonly TableSchema Charge = new(
        "ChargeAbilities", RecordSize: 2, RecordCount: 8,
        Fields: [FieldDef.U8("ChargeTicks", 0), FieldDef.U8("ChargeBonus", 1)],
        Source: "FFTPatcher Datatypes/Abilities/Ability.cs (ChargeTicks, ChargeBonus)") { FirstId = 0x196, RecordNames = Names.Abilities };

    public static readonly TableSchema Effects = new(
        "AbilityEffects", RecordSize: 2, RecordCount: 0x1C6,
        Fields: [FieldDef.U16("Effect", 0)],
        Source: "FFTPatcher Datatypes/Abilities/AllAbilities.cs (ToEffectsByteArray)") { RecordNames = Names.Abilities };

    public static readonly TableSchema Animations = new(
        "AbilityAnimations", RecordSize: 3, RecordCount: 0x1C6,
        Fields: [FieldDef.U8("Byte1", 0), FieldDef.U8("Byte2", 1), FieldDef.U8("Byte3", 2)],
        Source: "FFTPatcher Datatypes/Abilities/Animation.cs, AllAnimations.cs") { RecordNames = Names.Abilities };
}
