namespace TICPatcher.Core.Tables;

public enum FieldKind
{
    /// <summary>One unsigned byte.</summary>
    Byte,

    /// <summary>Two bytes, little-endian.</summary>
    UInt16,

    /// <summary>Bit flags over one or more bytes, most significant bit first.</summary>
    Flags,
}

/// <summary>
/// One field of a fixed-size record. For <see cref="FieldKind.Flags"/>,
/// <see cref="FlagNames"/> lists a name per bit, most significant bit of the
/// first byte first; null entries are unknown bits that are kept as they are.
/// </summary>
public sealed record FieldDef(string Name, int Offset, FieldKind Kind, int Size = 1, IReadOnlyList<string?>? FlagNames = null)
{
    public static FieldDef U8(string name, int offset) => new(name, offset, FieldKind.Byte);
    public static FieldDef U16(string name, int offset) => new(name, offset, FieldKind.UInt16, 2);
    public static FieldDef Flags(string name, int offset, IReadOnlyList<string?> names) =>
        new(name, offset, FieldKind.Flags, (names.Count + 7) / 8, names);
}

/// <summary>
/// A table of fixed-size records, e.g. the 169 jobs of 49 bytes each.
/// <see cref="Source"/> says where the layout comes from, so every fact can be checked.
/// </summary>
public sealed record TableSchema(string Name, int RecordSize, int RecordCount, IReadOnlyList<FieldDef> Fields, string Source)
{
    public FieldDef Field(string name) =>
        Fields.FirstOrDefault(f => f.Name == name) ?? throw new KeyNotFoundException($"{Name} has no field {name}");
}
