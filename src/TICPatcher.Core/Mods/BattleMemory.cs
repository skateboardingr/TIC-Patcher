using System.Runtime.Versioning;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Mods;

/// <summary>
/// The battles (ENTD) as the running game holds them: it loads all four battle
/// files from data\enhanced\0002.pac at start-up into one block of its own memory
/// (0x50000 bytes, battle 0 first), and takes each battle's units from there.
/// Measured on build 24304444 with TICScan --find-file and --peek.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BattleMemory
{
    private const int BattleSize = Battles.UnitsPerBattle * 40;

    // Pairs of battles spread over all four files, looked for together: a mod
    // (or an earlier injection) may have changed some of them.
    private static readonly int[] ProbeBattles = [0x001, 0x041, 0x081, 0x0C1, 0x101, 0x141, 0x181, 0x1C1];

    /// <summary>
    /// Where the game's battle block is, from the original battles, or null if it isn't
    /// loaded yet or the places found disagree (then nothing is guessed).
    /// </summary>
    /// <param name="insideGame">Searching from inside the game (TIC Loader): then TIC Loader's own
    /// copies (its arrays, and the buffers the search reads memory into) are in the same memory. They
    /// all live in .NET's managed heap, so hits there are skipped; the game's block is in its own heap.</param>
    public static long? Find(ProcessMemory memory, byte[] originalBattles, bool insideGame = false, Action<string>? why = null)
    {
        var probes = ProbeBattles
            .Select(b => (Offset: b * BattleSize, Bytes: originalBattles.AsSpan(b * BattleSize, 2 * BattleSize).ToArray()))
            .Where(p => p.Bytes.Any(x => x != 0))
            .ToList();
        var managed = insideGame ? memory.ManagedHeapBases() : [];
        var hits = probes.ToDictionary(p => p.Offset, _ => new List<long>());
        foreach (var (start, block) in memory.ScanBlocks(0, 0x7FFF_FFFF_FFFF, privateOnly: true))
            foreach (var (offset, probe) in probes)
                for (var at = block.AsSpan().IndexOf(probe); at >= 0;)
                {
                    if (managed.Count == 0 || !managed.Contains(memory.AllocationBase(start + at)))
                        hits[offset].Add(start + at - offset);
                    var next = block.AsSpan(at + 1).IndexOf(probe);
                    at = next < 0 ? -1 : at + 1 + next;
                }
        // Each probe found once (unchanged ones), all pointing at the same block.
        var places = hits.Values.Where(h => h.Count == 1).Select(h => h[0]).Distinct().ToList();
        if (places.Count == 1 && hits.Values.All(h => h.Count <= 1))
            return places[0];
        why?.Invoke($"pieces found {string.Join(", ", hits.Select(h => $"{h.Key / BattleSize:X3}:{h.Value.Count}"))}; places {string.Join(", ", hits.Values.SelectMany(h => h).Distinct().Select(a => $"0x{a:X}"))}");
        return null;
    }

    /// <summary>What the block should hold: the original battles with the edits applied.</summary>
    public static byte[] Wanted(byte[] originalBattles, TableEdits edits)
    {
        var wanted = (byte[])originalBattles.Clone();
        edits.ApplyTo(Battles.Units.Name, wanted, Battles.Units.RecordSize);
        return wanted;
    }

    /// <summary>Writes the battles the mod wants where they differ. Returns the bytes changed.</summary>
    public static int Apply(ProcessMemory memory, long address, byte[] wanted) =>
        memory.WriteChanges(address, memory.Read(address, wanted.Length), wanted);

    /// <summary>Whether the block at <paramref name="address"/> still holds exactly <paramref name="wanted"/>.</summary>
    public static bool Holds(ProcessMemory memory, long address, byte[] wanted)
    {
        try
        {
            return memory.Read(address, wanted.Length).AsSpan().SequenceEqual(wanted);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // freed
        }
    }
}
