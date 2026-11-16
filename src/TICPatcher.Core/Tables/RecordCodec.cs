namespace TICPatcher.Core.Tables;

/// <summary>
/// Reads and writes field values in a record's bytes. Numbers are decimal
/// strings; flags are the set bits' names joined by ", " ("" for none).
/// </summary>
public static class RecordCodec
{
    public static string Read(FieldDef field, ReadOnlySpan<byte> record) => field.Kind switch
    {
        FieldKind.Byte => record[field.Offset].ToString(),
        FieldKind.UInt16 => (record[field.Offset] | record[field.Offset + 1] << 8).ToString(),
        FieldKind.Flags => string.Join(", ", ReadFlags(field, record)),
        FieldKind.Bits => ((record[field.Offset] & field.Mask) >> Shift(field.Mask)).ToString(),
        FieldKind.AbilityId => (record[field.Offset] | ((record[field.HighOffset] & field.HighMask) != 0 ? 0x100 : 0)).ToString(),
        _ => throw new NotSupportedException(field.Kind.ToString()),
    };

    public static IEnumerable<string> ReadFlags(FieldDef field, ReadOnlySpan<byte> record)
    {
        var names = field.FlagNames ?? [];
        var set = new List<string>();
        for (var bit = 0; bit < names.Count; bit++)
        {
            if (names[bit] is { } name && IsSet(record, field.Offset, bit))
                set.Add(name);
        }
        return set;
    }

    public static void Write(FieldDef field, Span<byte> record, string value)
    {
        switch (field.Kind)
        {
            case FieldKind.Byte:
                record[field.Offset] = byte.Parse(value.Trim());
                break;
            case FieldKind.UInt16:
                var number = ushort.Parse(value.Trim());
                record[field.Offset] = (byte)number;
                record[field.Offset + 1] = (byte)(number >> 8);
                break;
            case FieldKind.Flags:
                var wanted = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
                var names = field.FlagNames ?? [];
                foreach (var unknown in wanted.Where(w => !names.Contains(w)))
                    throw new ArgumentException($"{field.Name} has no flag {unknown}");
                for (var bit = 0; bit < names.Count; bit++)
                {
                    // Unknown bits (null names) keep their current value.
                    if (names[bit] is { } name)
                        SetBit(record, field.Offset, bit, wanted.Contains(name));
                }
                break;
            case FieldKind.Bits:
                var bits = byte.Parse(value.Trim());
                var max = field.Mask >> Shift(field.Mask);
                if (bits > max)
                    throw new OverflowException($"{field.Name} is at most {max}");
                record[field.Offset] = (byte)((record[field.Offset] & ~field.Mask) | (bits << Shift(field.Mask)));
                break;
            case FieldKind.AbilityId:
                var id = ushort.Parse(value.Trim());
                if (id > 0x1FF)
                    throw new OverflowException($"{field.Name} is at most 511 (0x1FF)");
                record[field.Offset] = (byte)id;
                if (id > 0xFF)
                    record[field.HighOffset] |= field.HighMask;
                else
                    record[field.HighOffset] &= (byte)~field.HighMask;
                break;
            default:
                throw new NotSupportedException(field.Kind.ToString());
        }
    }

    private static int Shift(byte mask) => System.Numerics.BitOperations.TrailingZeroCount(mask);

    private static bool IsSet(ReadOnlySpan<byte> record, int offset, int bit) =>
        (record[offset + bit / 8] & (0x80 >> (bit % 8))) != 0;

    private static void SetBit(Span<byte> record, int offset, int bit, bool on)
    {
        var mask = (byte)(0x80 >> (bit % 8));
        if (on)
            record[offset + bit / 8] |= mask;
        else
            record[offset + bit / 8] &= (byte)~mask;
    }
}
