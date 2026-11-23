namespace TICPatcher.Core.Tables.Schemas;

/// <summary>
/// The job table, in its War of the Lions (PSP) layout: 169 jobs of 49 bytes.
/// The Ivalice Chronicles keeps this layout (see docs/findings.md).
///
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by
/// Joe Davidson and Glain: Datatypes/Job/Job.cs, Datatypes/Job/Equipment.cs,
/// Datatypes/Status/Statuses.cs, Datatypes/Elements.cs. Field names follow FFTPatcher's.
/// </summary>
public static class Jobs
{
    public static readonly IReadOnlyList<string?> Equipment =
    [
        "Unused", "Knife", "NinjaBlade", "Sword", "KnightsSword", "Katana", "Axe", "Rod",
        "Staff", "Flail", "Gun", "Crossbow", "Bow", "Instrument", "Book", "Polearm",
        "Pole", "Bag", "Cloth", "Shield", "Helmet", "Hat", "HairAdornment", "Armor",
        "Clothing", "Robe", "Shoes", "Armguard", "Ring", "Armlet", "Cloak", "Perfume",
        null, null, null, "FellSword", "LipRouge", null, null, null,
    ];

    public static readonly IReadOnlyList<string?> Statuses =
    [
        "NoEffect", "Crystal", "Dead", "Undead", "Charging", "Jump", "Defending", "Performing",
        "Petrify", "Invite", "Darkness", "Confusion", "Silence", "BloodSuck", "DarkEvilLooking", "Treasure",
        "Oil", "Float", "Reraise", "Transparent", "Berserk", "Chicken", "Frog", "Critical",
        "Poison", "Regen", "Protect", "Shell", "Haste", "Slow", "Stop", "Wall",
        "Faith", "Innocent", "Charm", "Sleep", "DontMove", "DontAct", "Reflect", "DeathSentence",
    ];

    public static readonly IReadOnlyList<string?> Elements =
        ["Fire", "Lightning", "Ice", "Wind", "Earth", "Water", "Holy", "Dark"];

    public static readonly TableSchema Schema = new(
        "Jobs",
        RecordSize: 49,
        RecordCount: 169,
        Fields:
        [
            FieldDef.U8("SkillSet", 0) with { NameList = Names.SkillSets, LinksTo = "SkillSets" },
            FieldDef.U16("InnateA", 1) with { NameList = Names.Abilities, LinksTo = "Abilities" },
            FieldDef.U16("InnateB", 3) with { NameList = Names.Abilities, LinksTo = "Abilities" },
            FieldDef.U16("InnateC", 5) with { NameList = Names.Abilities, LinksTo = "Abilities" },
            FieldDef.U16("InnateD", 7) with { NameList = Names.Abilities, LinksTo = "Abilities" },
            FieldDef.Flags("Equipment", 9, Equipment),
            FieldDef.U8("HPConstant", 14),
            FieldDef.U8("HPMultiplier", 15),
            FieldDef.U8("MPConstant", 16),
            FieldDef.U8("MPMultiplier", 17),
            FieldDef.U8("SpeedConstant", 18),
            FieldDef.U8("SpeedMultiplier", 19),
            FieldDef.U8("PAConstant", 20),
            FieldDef.U8("PAMultiplier", 21),
            FieldDef.U8("MAConstant", 22),
            FieldDef.U8("MAMultiplier", 23),
            FieldDef.U8("Move", 24),
            FieldDef.U8("Jump", 25),
            FieldDef.U8("CEvade", 26),
            FieldDef.Flags("PermanentStatus", 27, Statuses),
            FieldDef.Flags("StatusImmunity", 32, Statuses),
            FieldDef.Flags("StartingStatus", 37, Statuses),
            FieldDef.Flags("AbsorbElement", 42, Elements),
            FieldDef.Flags("CancelElement", 43, Elements),
            FieldDef.Flags("HalfElement", 44, Elements),
            FieldDef.Flags("WeakElement", 45, Elements),
            FieldDef.U8("MPortrait", 46),
            FieldDef.U8("MPalette", 47),
            FieldDef.U8("MGraphic", 48),
        ],
        Source: "FFTPatcher Datatypes/Job/Job.cs (PSP layout)") { RecordNames = Names.Jobs };

    /// <summary>Job ids of the generic jobs, Squire (0x4A) to Mime (0x5D).</summary>
    public const int FirstGenericJob = 0x4A;
    public const int LastGenericJob = 0x5D;

    /// <summary>
    /// Formation (party screen) sprites of the special jobs 0x00-0x49, the story characters':
    /// two tables, one byte and one 16-bit value per job. A job left at 0 in the first shows
    /// Ramza (chapter 1) on the party screen, e.g. guest-only characters made to join.
    /// </summary>
    public static readonly TableSchema FormationSprites1 = new(
        "JobFormationSprites1", RecordSize: 1, RecordCount: 0x4A,
        Fields: [FieldDef.U8("FormationSprite", 0)],
        Source: "FFTPatcher Datatypes/Job/Job.cs (FormationSprites1); located in TIC by FFTPatcher's PSX defaults") { RecordNames = Names.Jobs };

    public static readonly TableSchema FormationSprites2 = new(
        "JobFormationSprites2", RecordSize: 2, RecordCount: 0x4A,
        Fields: [FieldDef.U16("FormationSprite2", 0)],
        Source: "FFTPatcher Datatypes/Job/Job.cs (FormationSprites2); found byte for byte in TIC") { RecordNames = Names.Jobs };
}
