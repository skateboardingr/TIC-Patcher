using System.Text;
using TICPatcher.Core.Mods;

namespace TICPatcher.Core.Text;

/// <summary>A text table found in the game's memory: where it is and its rows (id → row offset from the table start).</summary>
public sealed record FoundTable(TextTable Table, long Address, IReadOnlyDictionary<int, int> Rows);

/// <summary>
/// Finds the game's loaded text tables in memory and works out how to change
/// their text. New text is written to fresh memory and only the edited field's
/// pointer changes: the game shares identical strings between rows (two jobs
/// called "Squire" point at the same text), so editing a string in place could
/// rename more than one thing.
/// </summary>
public static class TextPatcher
{
    private const int Sample = 40;

    /// <summary>Text tables in a block of memory read from <paramref name="blockStart"/>.</summary>
    public static IEnumerable<FoundTable> Find(byte[] block, long blockStart)
    {
        var magic = Encoding.ASCII.GetBytes(NxdTable.Magic);
        for (var at = block.AsSpan().IndexOf(magic); at >= 0;)
        {
            if (Recognise(block, at) is { } found)
                yield return found with { Address = blockStart + at };
            var next = block.AsSpan(at + 1).IndexOf(magic);
            at = next < 0 ? -1 : at + 1 + next;
        }
    }

    private static FoundTable? Recognise(byte[] block, int start)
    {
        if (start + 0x30 > block.Length || BitConverter.ToInt32(block, start + 0x20) != 0x30)
            return null;
        var count = BitConverter.ToInt32(block, start + 0x24);
        foreach (var table in TextTables.List.Where(t => t.Rows == count))
        {
            if (start + 0x30 + count * 8 > block.Length)
                continue;
            var rows = NxdTable.Rows(block, start);
            if (rows.Values.Any(r => r < 0 || r + 0x40 > block.Length))
                continue;
            var ordered = rows.OrderBy(r => r.Value).Select(r => r.Value).ToList();
            if (table.RowLength > 0 && ordered.Zip(ordered.Skip(1)).Take(Sample).Any(p => p.Second - p.First != table.RowLength))
                continue;
            if (rows.Values.Take(Sample).All(row => table.Fields.All(f => PointsAtText(block, row, table.Base, f.Offset))))
                return new FoundTable(table, start, rows.ToDictionary(r => r.Key, r => r.Value - start));
        }
        return null;
    }

    // A text field must point at the start of a NUL-terminated string.
    private static bool PointsAtText(byte[] block, int row, int baseOffset, int field)
    {
        var target = (long)row + baseOffset + BitConverter.ToUInt32(block, row + field);
        return target > 0 && target < block.Length && block[target - 1] == 0 && Array.IndexOf(block, (byte)0, (int)target) >= 0;
    }

    /// <summary>The new strings a table needs: all of them laid out in one buffer, NUL-terminated.</summary>
    public static (byte[] Text, List<(int Id, TextField Field, int At)> Strings) NewText(FoundTable found, TableEdits edits)
    {
        var buffer = new List<byte>();
        var strings = new List<(int, TextField, int)>();
        foreach (var (table, id, field, value) in edits.AllText)
        {
            if (table != found.Table.Name || !found.Rows.ContainsKey(id))
                continue;
            strings.Add((id, found.Table.Field(field), buffer.Count));
            buffer.AddRange(Encoding.UTF8.GetBytes(value));
            buffer.Add(0);
        }
        return ([.. buffer], strings);
    }

    /// <summary>
    /// The pointer writes that make a found table use new strings placed at
    /// <paramref name="textAddress"/>: (address, new u32 value). Throws if the
    /// text is out of the pointers' reach (it must sit after the table, within 4 GB).
    /// </summary>
    public static List<(long Address, uint Value)> Pointers(FoundTable found, List<(int Id, TextField Field, int At)> strings, long textAddress)
    {
        var writes = new List<(long, uint)>();
        foreach (var (id, field, at) in strings)
        {
            var row = found.Address + found.Rows[id];
            var offset = textAddress + at - (row + found.Table.Base);
            if (offset <= 0 || offset > uint.MaxValue)
                throw new InvalidOperationException($"New text at 0x{textAddress:X} is out of reach of {found.Table.Name} at 0x{found.Address:X}.");
            writes.Add((row + field.Offset, (uint)offset));
        }
        return writes;
    }
}
