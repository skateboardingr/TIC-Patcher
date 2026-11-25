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
        Fields: [FieldDef.U8("FormationSprite", 0) with { LinksTo = "FormationSlotSprites" }],
        Source: "FFTPatcher Datatypes/Job/Job.cs (FormationSprites1); located in TIC by FFTPatcher's PSX defaults") { RecordNames = Names.Jobs };

    public static readonly TableSchema FormationSprites2 = new(
        "JobFormationSprites2", RecordSize: 2, RecordCount: 0x4A,
        Fields: [FieldDef.U16("FormationSprite2", 0) with { LinksTo = "SlotSprites" }],
        Source: "FFTPatcher Datatypes/Job/Job.cs (FormationSprites2); found byte for byte in TIC") { RecordNames = Names.Jobs };

    /// <summary>
    /// The sprite shown for each formation slot (a job's FormationSprite): 24 story-character
    /// slots, then the generic jobs (male, female). The game reads this list only for slots
    /// 0 to 0x51 and goes to <see cref="SlotSprites"/> for higher ones, but the formation
    /// screen froze on those in testing, so a guest who joins should get a slot here.
    /// </summary>
    public static readonly TableSchema FormationSlotSprites = new(
        "FormationSlotSprites", RecordSize: 4, RecordCount: 0x52,
        Fields: [FieldDef.U16("Sprite", 0) with { NameList = Names.SpriteFiles, Specials = NoSprite }],
        Source: "TIC's exe, read at +0x2F99A9, +0x339E00 and +0x3994E9 for slots up to 0x51")
    {
        RecordName = SlotName,
    };

    /// <summary>The sprite for each FormationSprite2 slot (and FormationSprite slots above 0x51), e.g. 0x89 Isilud.</summary>
    public static readonly TableSchema SlotSprites = new(
        "SlotSprites", RecordSize: 4, RecordCount: 0xA0,
        Fields: [FieldDef.U16("Sprite", 0) with { NameList = Names.SpriteFiles, Specials = NoSprite }],
        Source: "TIC's exe, read by the function at +0x274028 for slots up to 0x9F")
    {
        RecordName = SlotName,
    };

    /// <summary>
    /// The formation slot of each unit sprite, read by the party screen for a story-character unit
    /// (sprite set below 0x80). Slots below 0x3E are drawn from <see cref="FormationSlotSprites"/>;
    /// higher ones (the guests' 0x78-0x93) are treated like a generic unit and crash the party screen,
    /// so a guest who joins needs a low slot here, e.g. Isilud 0x26 -> 0x08.
    /// </summary>
    public static readonly TableSchema SpriteFormationSlots = new(
        "SpriteFormationSlots", RecordSize: 2, RecordCount: 0xD3,
        Fields: [FieldDef.U16("Slot", 0) with { LinksTo = "FormationSlotSprites" }],
        Source: "TIC's exe, read at +0x300F39 by the party screen (crash at +0x301451 for a slot of 0x3E or more)")
    {
        RecordNames = Names.SpriteFiles,
    };

    /// <summary>
    /// The game keeps six copies of the sprite -> slot list (<see cref="FormationSprites2"/>,
    /// <see cref="SpriteFormationSlots"/> and these four), each read by different code. Values
    /// above 0x51 are looked up in <see cref="SlotSprites"/>, e.g. 0x80 = Wiegraf ch. 2/3. The copy
    /// at +0x6807C0 seems to pick the unit's face on the party screen.
    /// </summary>
    public static readonly IReadOnlyList<TableSchema> SpriteSlotCopies =
    [
        SpriteSlotCopy("SpriteSlots2", "+0x2C0853, then the slot sprite function +0x274028 (party screen face)"),
        SpriteSlotCopy("SpriteSlots3", "no direct reader found"),
        SpriteSlotCopy("SpriteSlots4", "+0x2DB41E"),
        SpriteSlotCopy("SpriteSlots5", "+0x32AC23"),
    ];

    private static TableSchema SpriteSlotCopy(string name, string readBy) => new(
        name, RecordSize: 2, RecordCount: 0xD3,
        Fields: [FieldDef.U16("Slot", 0) with { LinksTo = "SlotSprites" }],
        Source: $"TIC's exe, a copy of the sprite -> slot list read at {readBy}")
    {
        RecordNames = Names.SpriteFiles,
    };

    // A property, as the tables above are built before a static field declared here would be.
    static IReadOnlyDictionary<int, string> NoSprite => new Dictionary<int, string> { [0xFFFF] = "None" };

    static string SlotName(int slot) =>
        slot < 24 ? $"Slot {slot:X2} (story character)"
        : slot < 64 ? $"Slot {slot:X2} ({Names.Get(Names.Jobs, 0x4A + (slot - 24) / 2)}, {(slot % 2 == 0 ? "male" : "female")})"
        : $"Slot {slot:X2}";
}
