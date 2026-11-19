using System.Text;

namespace TICPatcher.Core.Text;

/// <summary>
/// One of the game's .nxd tables (see docs/archives.md): an index of
/// (id, row) pairs, rows of fields, and a pool of NUL-terminated UTF-8
/// strings that text fields point into.
/// </summary>
public sealed class NxdTable
{
    public const string Magic = "NXDF";

    private readonly byte[] _bytes;
    private readonly Dictionary<int, int> _rows; // id -> row start

    private NxdTable(byte[] bytes, Dictionary<int, int> rows)
    {
        _bytes = bytes;
        _rows = rows;
    }

    public IReadOnlyCollection<int> Ids => _rows.Keys;

    public static NxdTable Parse(byte[] bytes)
    {
        if (bytes.Length < 0x30 || Encoding.ASCII.GetString(bytes, 0, 4) != Magic)
            throw new FormatException("Not an .nxd table.");
        return new NxdTable(bytes, Rows(bytes));
    }

    /// <summary>The (id → row start) index of a table in <paramref name="bytes"/>, starting at <paramref name="start"/>.</summary>
    public static Dictionary<int, int> Rows(ReadOnlySpan<byte> bytes, int start = 0)
    {
        var indexAt = BitConverter.ToInt32(bytes[(start + 0x20)..]);
        var count = BitConverter.ToInt32(bytes[(start + 0x24)..]);
        var rows = new Dictionary<int, int>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = start + indexAt + i * 8;
            var id = BitConverter.ToInt32(bytes[entry..]);
            rows[id] = entry + 4 + BitConverter.ToInt32(bytes[(entry + 4)..]);
        }
        return rows;
    }

    /// <summary>A text field of a row, or null if the table has no such row.</summary>
    public string? Text(TextTable table, int id, TextField field)
    {
        if (!_rows.TryGetValue(id, out var row))
            return null;
        var at = row + table.Base + BitConverter.ToInt32(_bytes, row + field.Offset);
        var end = Array.IndexOf(_bytes, (byte)0, at);
        return Encoding.UTF8.GetString(_bytes, at, end - at);
    }

    /// <summary>Every row's text, as table → id → field → text.</summary>
    public Dictionary<int, Dictionary<string, string>> AllText(TextTable table) =>
        _rows.Keys.ToDictionary(id => id, id => table.Fields.ToDictionary(f => f.Name, f => Text(table, id, f)!));
}
