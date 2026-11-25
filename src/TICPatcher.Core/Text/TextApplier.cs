using System.Runtime.Versioning;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Mods;

namespace TICPatcher.Core.Text;

/// <summary>
/// Applies a mod's text edits to the game's loaded text tables (names and
/// descriptions). See <see cref="Relocate"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TextApplier
{
    /// <summary>
    /// Gives the game rebuilt copies of its text tables: for each loaded table the
    /// game points at (through records holding the table's address and size), a
    /// copy with the new strings appended and the edited fields repointed is put in
    /// fresh memory, and every pointer into the old table (the records, and the
    /// game's per-row lookup, which points at index entries) is moved to the
    /// same place in the copy. Text can grow freely, since the copy is self-contained.
    /// </summary>
    /// <param name="done">Tables already moved (by name) and the copies made (by address), kept between calls
    /// so a table the game loads later is caught and a copy is never moved again.</param>
    /// <returns>How many tables the mod changes are now done.</returns>
    /// <param name="insideGame">Running inside the game (TIC Loader): skip hits in .NET's managed heap,
    /// which are this code's own copies of what it read, not the game's tables or pointers.</param>
    public static int Relocate(ProcessMemory memory, TableEdits edits, Done done, Action<string> log, bool insideGame = false)
    {
        var managed = insideGame ? memory.ManagedHeapBases() : [];
        bool Ours(long address) => managed.Count > 0 && managed.Contains(memory.AllocationBase(address));
        var wanted = edits.AllText.Select(e => e.Table).Where(t => !done.Tables.Contains(t)).ToHashSet();
        if (wanted.Count == 0)
            return done.Tables.Count;
        // Reads the game's working memory a block at a time, in two passes, so
        // nothing like a copy of the whole game is held at once.
        IEnumerable<(long Start, ArraySegment<byte> Bytes)> Blocks() => memory.ScanBlocks(0, 0x7FFF_FFFF_FFFF, privateOnly: true);

        // 1. The loaded text tables the mod changes: a window after each table header is copied
        // out and checked (text tables are tens of KB; the scan buffer is reused for every block).
        const int Window = 4 << 20;
        var tables = new Dictionary<long, FoundTable>();
        var magic = System.Text.Encoding.ASCII.GetBytes(NxdTable.Magic);
        foreach (var (start, block) in Blocks())
            for (var at = block.AsSpan().IndexOf(magic); at >= 0;)
            {
                var window = block.AsSpan(at, Math.Min(Window, block.Count - at)).ToArray();
                var table = TextPatcher.At(window, start + at);
                if (table is not null && wanted.Contains(table.Table.Name) && !done.Copies.Contains(table.Address) && !Ours(table.Address))
                    tables[table.Address] = table;
                var next = block.AsSpan(at + 1).IndexOf(magic);
                at = next < 0 ? -1 : at + 1 + next;
            }

        if (tables.Count == 0)
            return done.Tables.Count;

        // 2. In one more pass: records pointing at a table (an 8-byte address followed by
        // the table's size), and every pointer into a table (the game's per-row lookups),
        // collected up to a generous size and trimmed once the real sizes are known.
        const long MaxTable = 0x100_0000;
        var starts = tables.Keys.Order().ToArray();
        long low = starts[0], high = starts[^1] + MaxTable;
        var records = new Dictionary<long, List<(long At, long Size)>>();
        var pointers = starts.ToDictionary(a => a, _ => new List<(long At, long Value)>());
        foreach (var (start, block) in Blocks())
            for (var i = 0; i + 8 <= block.Count; i += 8)
            {
                var bytes = block.AsSpan();
                var value = BitConverter.ToInt64(bytes[i..]);
                if (value < low || value >= high)
                    continue; // nearly every word: not near any table
                var index = Array.BinarySearch(starts, value);
                if (index >= 0)
                {
                    var size = i + 16 <= bytes.Length ? BitConverter.ToInt64(bytes[(i + 8)..]) : 0;
                    if (size is > 0x30 and < MaxTable && !Ours(start + i))
                        (records.TryGetValue(value, out var list) ? list : records[value] = []).Add((start + i, size));
                    continue;
                }
                var owner = starts[~index - 1 < 0 ? 0 : ~index - 1];
                if (~index > 0 && value - owner < MaxTable && !Ours(start + i))
                    pointers[owner].Add((start + i, value));
            }
        // Only tables the game holds this way, with one agreed size (not the raw archive data).
        var held = records.Where(r => r.Value.Select(x => x.Size).Distinct().Count() == 1)
            .ToDictionary(r => r.Key, r => (Size: r.Value[0].Size, Records: r.Value));
        var inside = held.ToDictionary(h => h.Key, h => pointers[h.Key].Where(p => p.Value < h.Key + h.Value.Size).ToList());
        log($"text: {tables.Count} table(s) found, {held.Count} held by the game");

        foreach (var (address, (size, refs)) in held)
        {
            var table = tables[address];
            var (text, strings) = TextPatcher.NewText(table, edits);
            if (strings.Count == 0)
                continue;
            var copy = memory.Read(address, (int)size).Concat(text).ToArray();
            // Wherever Windows chooses: among the game's own addresses, not a round number
            // that plain integers elsewhere could be mistaken for.
            var at = memory.Allocate(copy.Length);
            if (at == 0)
            {
                log($"{table.Table.Name}: couldn't allocate memory");
                continue;
            }
            foreach (var (pointer, value) in TextPatcher.Pointers(table with { Address = at }, strings, at + size))
                BitConverter.GetBytes(value).CopyTo(copy, pointer - at);
            memory.Write(at, copy);
            // A place that has since been freed is skipped rather than stopping everything.
            var failed = 0;
            void TryWrite(long where, byte[] bytes)
            {
                try
                {
                    memory.Write(where, bytes);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    failed++;
                }
            }
            foreach (var (recordAt, _) in refs)
            {
                TryWrite(recordAt, BitConverter.GetBytes(at));
                TryWrite(recordAt + 8, BitConverter.GetBytes((long)copy.Length));
            }
            foreach (var (pointerAt, value) in inside[address])
                TryWrite(pointerAt, BitConverter.GetBytes(at + (value - address)));
            if (failed > 0)
                log($"{table.Table.Name}: {failed} place(s) couldn't be written (freed meanwhile); skipped");
            done.Tables.Add(table.Table.Name);
            done.Copies.Add(at);
            log($"{table.Table.Name}: {strings.Count} text change(s); moved to a {copy.Length}-byte copy ({refs.Count} record(s), {inside[address].Count} pointer(s) updated)");
        }
        return done.Tables.Count;
    }

    /// <summary>What <see cref="Relocate"/> has done so far.</summary>
    public sealed class Done
    {
        public HashSet<string> Tables { get; } = new(StringComparer.Ordinal);
        public HashSet<long> Copies { get; } = [];
    }
}
