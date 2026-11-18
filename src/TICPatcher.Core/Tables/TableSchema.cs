namespace TICPatcher.Core.Tables;

public enum FieldKind
{
    /// <summary>One unsigned byte.</summary>
    Byte,

    /// <summary>Two bytes, little-endian.</summary>
    UInt16,

    /// <summary>Bit flags over one or more bytes, most significant bit first.</summary>
    Flags,

    /// <summary>A number in some bits of one byte (<see cref="FieldDef.Mask"/>), e.g. a nibble.</summary>
    Bits,

    /// <summary>
    /// A 9-bit ability id: the low 8 bits at <see cref="FieldDef.Offset"/>, the
    /// 9th in another byte (<see cref="FieldDef.HighOffset"/>, <see cref="FieldDef.HighMask"/>),
    /// as skill sets and monster skills store them.
    /// </summary>
    AbilityId,
}

/// <summary>
/// One field of a fixed-size record. For <see cref="FieldKind.Flags"/>,
/// <see cref="FlagNames"/> lists a name per bit, most significant bit of the
/// first byte first; null entries are unknown bits that are kept as they are.
/// </summary>
public sealed record FieldDef(string Name, int Offset, FieldKind Kind, int Size = 1, IReadOnlyList<string?>? FlagNames = null)
{
    public byte Mask { get; init; } = 0xFF;

    /// <summary>The <see cref="Names"/> list this field's value is an id in, if any (e.g. a job's skill set).</summary>
    public string? NameList { get; init; }

    /// <summary>A friendlier name shown in the editor; mods keep using <see cref="Name"/>.</summary>
    public string? Label { get; init; }

    /// <summary>The table whose record this field's value selects (e.g. an item's attributes entry), for jumping to it.</summary>
    public string? LinksTo { get; init; }

    /// <summary>
    /// The field means something else when another field of the record has a
    /// given value, e.g. a weapon's InflictStatus is the spell it casts when its
    /// Formula is 02.
    /// </summary>
    public FieldMeaning? When { get; init; }

    /// <summary>This field as it reads for a record (see <see cref="When"/>).</summary>
    public FieldDef For(Func<string, string> otherField) =>
        When is { } when && otherField(when.Field) == when.Value.ToString()
            ? this with { Label = when.Label, NameList = when.NameList, LinksTo = when.LinksTo, When = null }
            : this;
    public int HighOffset { get; init; }
    public byte HighMask { get; init; }

    public static FieldDef U8(string name, int offset) => new(name, offset, FieldKind.Byte);
    public static FieldDef U16(string name, int offset) => new(name, offset, FieldKind.UInt16, 2);
    public static FieldDef Flags(string name, int offset, IReadOnlyList<string?> names) =>
        new(name, offset, FieldKind.Flags, (names.Count + 7) / 8, names);
    public static FieldDef Bits(string name, int offset, byte mask) => new(name, offset, FieldKind.Bits) { Mask = mask };
    public static FieldDef Upper(string name, int offset) => Bits(name, offset, 0xF0);
    public static FieldDef Lower(string name, int offset) => Bits(name, offset, 0x0F);
    public static FieldDef Ability(string name, int offset, int highOffset, byte highMask) =>
        new(name, offset, FieldKind.AbilityId) { HighOffset = highOffset, HighMask = highMask, LinksTo = "Abilities" };
}

/// <summary>What a field means while <paramref name="Field"/> of the same record equals <paramref name="Value"/>.</summary>
public sealed record FieldMeaning(string Field, int Value, string Label, string? NameList, string? LinksTo);

/// <summary>
/// A table of fixed-size records, e.g. the 169 jobs of 49 bytes each.
/// Record ids start at <see cref="FirstId"/>, so a part table (e.g. weapons)
/// uses the same ids as the game. <see cref="Source"/> says where the layout
/// comes from, so every fact can be checked.
/// </summary>
public sealed record TableSchema(string Name, int RecordSize, int RecordCount, IReadOnlyList<FieldDef> Fields, string Source)
{
    public int FirstId { get; init; }

    /// <summary>The <see cref="Names"/> list naming this table's records, if any.</summary>
    public string? RecordNames { get; init; }

    /// <summary>Overrides <see cref="RecordNames"/> for tables named some other way.</summary>
    public Func<int, string?>? RecordName { get; init; }

    public string? NameOf(int id) => RecordName is { } name ? name(id) : RecordNames is { } list ? Names.Get(list, id) : null;

    public FieldDef Field(string name) =>
        Fields.FirstOrDefault(f => f.Name == name) ?? throw new KeyNotFoundException($"{Name} has no field {name}");

    public int Length => RecordSize * RecordCount;
}
