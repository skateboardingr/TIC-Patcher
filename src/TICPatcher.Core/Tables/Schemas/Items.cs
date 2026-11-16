namespace TICPatcher.Core.Tables.Schemas;

/// <summary>
/// Item tables. Item ids: 0x00-0x7F weapons, 0x80-0x8F shields, 0x90-0xCF
/// head and body armor, 0xD0-0xEF accessories, 0xF0-0xFD chemist items.
/// The part tables (Weapons, Shields, ...) use the same ids.
///
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by
/// Joe Davidson and Glain: Datatypes/Items/Item.cs, Weapon.cs, Shield.cs,
/// Armor.cs, Accessory.cs, ChemistItem.cs, AllItems.cs, ItemAttributes.cs,
/// StoreInventory.cs, PatcherLib/Datatypes/Shops.cs. Field names follow FFTPatcher's.
/// The Ivalice Chronicles' item attribute records are 26 bytes, not 25:
/// byte 25 is new: non-zero gives the wearer JP Boost (read from the game's code; see docs/unknowns.md).
/// </summary>
public static class Items
{
    public static readonly TableSchema Common = new(
        "Items",
        RecordSize: 12,
        RecordCount: 256,
        Fields:
        [
            FieldDef.U8("Palette", 0),
            FieldDef.U8("Graphic", 1),
            FieldDef.U8("EnemyLevel", 2),
            FieldDef.Flags("Flags", 3, ["Weapon", "Shield", "Head", "Body", "Accessory", "Blank1", "Rare", "Blank2"]),
            FieldDef.U8("SecondTableId", 4),
            FieldDef.U8("ItemType", 5),
            FieldDef.U8("Unknown1", 6),
            FieldDef.U8("SIA", 7),
            FieldDef.U16("Price", 8),
            FieldDef.U8("ShopAvailability", 10),
            FieldDef.U8("Unknown2", 11),
        ],
        Source: "FFTPatcher Datatypes/Items/Item.cs") { RecordNames = Names.Items };

    public static readonly TableSchema Weapons = new(
        "Weapons",
        RecordSize: 8,
        RecordCount: 0x80,
        Fields:
        [
            FieldDef.U8("Range", 0),
            FieldDef.Flags("Flags", 1, ["Striking", "Lunging", "Direct", "Arc", "TwoSwords", "TwoHands", "Throwable", "Force2Hands"]),
            FieldDef.U8("Formula", 2),
            FieldDef.U8("Unknown", 3),
            FieldDef.U8("WeaponPower", 4),
            FieldDef.U8("EvadePercentage", 5),
            FieldDef.Flags("Elements", 6, Jobs.Elements),
            FieldDef.U8("InflictStatus", 7),
        ],
        Source: "FFTPatcher Datatypes/Items/Weapon.cs, AllItems.cs") { RecordNames = Names.Items };

    public static readonly TableSchema Shields = new(
        "Shields", RecordSize: 2, RecordCount: 0x10,
        Fields: [FieldDef.U8("PhysicalBlockRate", 0), FieldDef.U8("MagicBlockRate", 1)],
        Source: "FFTPatcher Datatypes/Items/Shield.cs, AllItems.cs") { FirstId = 0x80, RecordNames = Names.Items };

    public static readonly TableSchema Armor = new(
        "Armor", RecordSize: 2, RecordCount: 0x40,
        Fields: [FieldDef.U8("HPBonus", 0), FieldDef.U8("MPBonus", 1)],
        Source: "FFTPatcher Datatypes/Items/Armor.cs, AllItems.cs") { FirstId = 0x90, RecordNames = Names.Items };

    public static readonly TableSchema Accessories = new(
        "Accessories", RecordSize: 2, RecordCount: 0x20,
        Fields: [FieldDef.U8("PhysicalEvade", 0), FieldDef.U8("MagicEvade", 1)],
        Source: "FFTPatcher Datatypes/Items/Accessory.cs, AllItems.cs") { FirstId = 0xD0, RecordNames = Names.Items };

    public static readonly TableSchema ChemistItems = new(
        "ChemistItems", RecordSize: 3, RecordCount: 0x0E,
        Fields: [FieldDef.U8("Formula", 0), FieldDef.U8("X", 1), FieldDef.U8("InflictStatus", 2)],
        Source: "FFTPatcher Datatypes/Items/ChemistItem.cs, AllItems.cs") { FirstId = 0xF0, RecordNames = Names.Items };

    public static readonly TableSchema Attributes = new(
        "ItemAttributes",
        RecordSize: 26,
        RecordCount: 0x55,
        Fields:
        [
            FieldDef.U8("PA", 0),
            FieldDef.U8("MA", 1),
            FieldDef.U8("Speed", 2),
            FieldDef.U8("Move", 3),
            FieldDef.U8("Jump", 4),
            FieldDef.Flags("PermanentStatus", 5, Jobs.Statuses),
            FieldDef.Flags("StatusImmunity", 10, Jobs.Statuses),
            FieldDef.Flags("StartingStatus", 15, Jobs.Statuses),
            FieldDef.Flags("AbsorbElement", 20, Jobs.Elements),
            FieldDef.Flags("CancelElement", 21, Jobs.Elements),
            FieldDef.Flags("HalfElement", 22, Jobs.Elements),
            FieldDef.Flags("WeakElement", 23, Jobs.Elements),
            FieldDef.Flags("StrongElement", 24, Jobs.Elements),
            FieldDef.U8("JPBoost", 25),
        ],
        Source: "FFTPatcher Datatypes/Items/ItemAttributes.cs; record size measured in TIC");

    /// <summary>
    /// The remaster's bonus items (preorder/DLC): five items stored apart from
    /// the main 256, using item attributes 0x50-0x54. Their ids are probably
    /// 0x100-0x104 (a list of item-id boundaries next to them ends at 0x100).
    /// Names as the game shows them, where known.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> BonusNames = new Dictionary<int, string>
    {
        [0x104] = "Ring of Aptitude",
    };

    public static readonly TableSchema Bonus = Common with
    {
        Name = "BonusItems", RecordCount = 5, FirstId = 0x100, RecordNames = null,
        RecordName = id => BonusNames.GetValueOrDefault(id),
        Source = "Found in TIC by its attribute ids; layout as FFTPatcher Datatypes/Items/Item.cs",
    };

    /// <summary>Weapon data for the two bonus swords, stored just before <see cref="Bonus"/>.</summary>
    public static readonly TableSchema BonusWeapons = Weapons with
    {
        Name = "BonusWeapons", RecordCount = 2, FirstId = 0x100, RecordNames = null,
        Source = "Found in TIC next to the bonus items; layout as FFTPatcher Datatypes/Items/Weapon.cs",
    };

    /// <summary>Which shops sell each item: a big-endian 16-bit set, Lesalia = 0x8000.</summary>
    public static readonly IReadOnlyList<string?> Shops =
    [
        "Lesalia", "Riovanes", "Igros", "Lionel", "Limberry", "Zeltennia", "Gariland", "Yardrow",
        "Goland", "Dorter", "Zaland", "Goug", "Warjilis", "Bervenia", "Zarghidas", "None",
    ];

    public static readonly TableSchema ShopInventories = new(
        "ShopInventories", RecordSize: 2, RecordCount: 256,
        Fields: [FieldDef.Flags("Shops", 0, Shops)],
        Source: "FFTPatcher Datatypes/Items/StoreInventory.cs, PatcherLib/Datatypes/Shops.cs") { RecordNames = Names.Items };
}
