using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using TICPatcher.Core.Tables;

namespace TICPatcher.Loader;

/// <summary>
/// A research tool: records which bytes of chosen tables the game reads, and
/// from where in its code. The tables' pages are marked PAGE_GUARD; each read
/// raises a guard-page exception that a vectored handler counts, then the
/// faulting instruction is single-stepped and the guard re-armed. No debugger.
///
/// Turned on by a watch.json next to tic.loader.dll:
/// <code>{ "Tables": ["ActionAbilities", "ItemAttributes"], "Minutes": 10 }</code>
/// The game runs slower while watching. Reads by other threads in the instant
/// a guard is down are missed, so counts are a sample, but a byte read even
/// once will almost surely show up.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class ReadWatcher
{
    public sealed record Config(string[]? Tables = null, double Minutes = 10, Range[]? Ranges = null, int[]? Records = null);

    // With "Records" in watch.json, code addresses are kept only for reads of those record ids
    // (the counts still cover every record), to see exactly which code reads them.
    private static HashSet<int>? _records;

    /// <summary>
    /// Memory that isn't an editable table, e.g. the party roster: <c>{ "Name": "Roster", "Offset": "0x11A7D10",
    /// "RecordSize": 600, "RecordCount": 54 }</c>, at a module offset. Code addresses are kept per byte
    /// offset modulo 256, so in records bigger than that, offsets 256 apart share a line.
    /// </summary>
    public sealed record Range(string Name, string Offset, int RecordSize, int RecordCount);

    private const uint GuardPageViolation = 0x80000001;
    private const uint SingleStep = 0x80000004;
    private const int ContinueExecution = -1;
    private const int ContinueSearch = 0;
    private const uint PageGuard = 0x100;
    private const int Slots = 1 << 14;

    private sealed record Watched(TableSchema Schema, long Start, long End);

    private static Watched[] _tables = [];
    private static (long Start, long Size, uint Protect)[] _pages = [];
    private static long _moduleStart;
    private static long _moduleEnd;
    private static volatile bool _active;

    // Counts, filled from the exception handler without allocating:
    // per (table, byte offset in record), per (table, record), and per (code address, table, offset).
    private static long[][] _byOffset = [];
    private static long[][] _byRecord = [];
    private static readonly long[] Keys = new long[Slots];
    private static readonly long[] Counts = new long[Slots];
    private static long _dropped;

    [ThreadStatic] private static bool _stepping;

    public static Config? Load(string folder)
    {
        var path = Path.Combine(folder, "watch.json");
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public static void Start(Config config, IReadOnlyDictionary<string, long> offsets, long moduleStart, long moduleEnd, string reportPath, Action<string> log)
    {
        var tables = new List<Watched>();
        foreach (var range in config.Ranges ?? [])
        {
            var at = Convert.ToInt64(range.Offset, 16);
            var schema = new TableSchema(range.Name, range.RecordSize, range.RecordCount, [], "watch.json range");
            tables.Add(new Watched(schema, moduleStart + at, moduleStart + at + (long)range.RecordSize * range.RecordCount));
        }
        foreach (var name in config.Tables ?? [])
        {
            if (!TICPatcher.Core.Mods.TableEdits.KnownTables.TryGetValue(name, out var schema) || !offsets.TryGetValue(name, out var offset))
            {
                log($"watch: {name} isn't a located table, skipped");
                continue;
            }
            tables.Add(new Watched(schema, moduleStart + offset, moduleStart + offset + schema.Length));
        }
        if (tables.Count == 0)
            return;

        _records = config.Records is { Length: > 0 } records ? [.. records] : null;
        _moduleStart = moduleStart;
        _moduleEnd = moduleEnd;
        _tables = [.. tables];
        _byOffset = tables.Select(t => new long[t.Schema.RecordSize]).ToArray();
        _byRecord = tables.Select(t => new long[t.Schema.RecordCount]).ToArray();
        _pages = PagesOf(tables);

        AddVectoredExceptionHandler(1, &Handler);
        _active = true;
        Arm();
        log($"watch: recording reads of {string.Join(", ", tables.Select(t => t.Schema.Name))} for {config.Minutes} minute(s); the game will be slower until then");

        new Thread(() =>
        {
            var until = DateTime.UtcNow.AddMinutes(config.Minutes);
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(TimeSpan.FromSeconds(30));
                WriteReport(reportPath, final: false);
            }
            _active = false;
            Disarm();
            WriteReport(reportPath, final: true);
            log($"watch: finished; report in {reportPath}");
        }) { IsBackground = true, Name = "TIC Loader watch" }.Start();
    }

    private static (long, long, uint)[] PagesOf(List<Watched> tables)
    {
        var pages = new SortedSet<long>();
        foreach (var t in tables)
            for (var page = t.Start & ~0xFFFL; page < t.End; page += 0x1000)
                pages.Add(page);
        var result = new List<(long, long, uint)>();
        foreach (var page in pages)
        {
            MEMORY_BASIC_INFORMATION info;
            VirtualQuery((void*)page, &info, (nuint)sizeof(MEMORY_BASIC_INFORMATION));
            var protect = info.Protect & ~PageGuard;
            if (result.Count > 0 && result[^1].Item1 + result[^1].Item2 == page && result[^1].Item3 == protect)
                result[^1] = (result[^1].Item1, result[^1].Item2 + 0x1000, protect);
            else
                result.Add((page, 0x1000, protect));
        }
        return [.. result];
    }

    private static void Arm()
    {
        foreach (var (start, size, protect) in _pages)
            VirtualProtect((void*)start, (nuint)size, protect | PageGuard, out _);
    }

    private static void Disarm()
    {
        foreach (var (start, size, protect) in _pages)
            VirtualProtect((void*)start, (nuint)size, protect, out _);
    }

    [UnmanagedCallersOnly]
    private static int Handler(EXCEPTION_POINTERS* pointers)
    {
        var record = pointers->ExceptionRecord;
        var code = *(uint*)record;
        if (code == GuardPageViolation)
        {
            var address = (long)*(ulong*)(record + 32 + 8); // ExceptionInformation[1]
            if (!OnWatchedPage(address))
                return ContinueSearch;
            if (_active)
            {
                Count(address, (long)*(ulong*)(record + 16)); // ExceptionAddress
                _stepping = true;
                *(uint*)(pointers->ContextRecord + 0x44) |= 0x100; // EFlags.TF: stop after this instruction
            }
            return ContinueExecution;
        }
        if (code == SingleStep && _stepping)
        {
            _stepping = false;
            *(uint*)(pointers->ContextRecord + 0x44) &= ~0x100u;
            if (_active)
                Arm();
            return ContinueExecution;
        }
        return ContinueSearch;
    }

    private static bool OnWatchedPage(long address)
    {
        foreach (var (start, size, _) in _pages)
            if (address >= start && address < start + size)
                return true;
        return false;
    }

    private static void Count(long address, long instruction)
    {
        for (var t = 0; t < _tables.Length; t++)
        {
            var table = _tables[t];
            if (address < table.Start || address >= table.End)
                continue;
            var offsetInTable = address - table.Start;
            var size = table.Schema.RecordSize;
            var offset = (int)(offsetInTable % size);
            Interlocked.Increment(ref _byOffset[t][offset]);
            Interlocked.Increment(ref _byRecord[t][(int)(offsetInTable / size)]);
            if (_records is not null && !_records.Contains((int)(offsetInTable / size) + table.Schema.FirstId))
                return;

            var key = (instruction << 16) | ((long)t << 8) | (long)(offset & 0xFF); // user-mode addresses fit in 48 bits
            var slot = (int)((ulong)(key * unchecked((long)0x9E3779B97F4A7C15)) >> 50);
            for (var probe = 0; probe < 64; probe++, slot = (slot + 1) & (Slots - 1))
            {
                var existing = Interlocked.CompareExchange(ref Keys[slot], key, 0);
                if (existing == 0 || existing == key)
                {
                    Interlocked.Increment(ref Counts[slot]);
                    return;
                }
            }
            Interlocked.Increment(ref _dropped);
            return;
        }
    }

    // Game code as a module offset; anything else (e.g. the C runtime's memcpy) as an absolute address.
    private static string Where(long instruction) =>
        instruction >= _moduleStart && instruction < _moduleEnd ? $"+0x{instruction - _moduleStart:X}" : $"0x{instruction:X}";

    private static void WriteReport(string path, bool final)
    {
        var text = new StringBuilder();
        text.AppendLine($"TIC Loader read watch, {(final ? "final" : "so far")}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine("Counts are reads seen; 0 means never seen read while watching.");
        for (var t = 0; t < _tables.Length; t++)
        {
            var schema = _tables[t].Schema;
            text.AppendLine();
            text.AppendLine($"== {schema.Name} ({schema.RecordSize}-byte records)");
            for (var offset = 0; offset < schema.RecordSize; offset++)
            {
                var fields = schema.Fields.Where(f => offset >= f.Offset && offset < f.Offset + f.Size).Select(f => f.Name);
                var code = Enumerable.Range(0, Slots)
                    .Where(s => Keys[s] != 0 && (int)(((ulong)Keys[s] >> 8) & 0xFF) == t && (int)(Keys[s] & 0xFF) == (offset & 0xFF))
                    .OrderByDescending(s => Counts[s]).Take(6)
                    .Select(s => $"{Where((long)((ulong)Keys[s] >> 16))} x{Counts[s]}");
                text.AppendLine($"  byte {offset,2} {string.Join("/", fields),-28} {_byOffset[t][offset],10}   {string.Join(", ", code)}");
            }
            var busiest = Enumerable.Range(0, schema.RecordCount).Where(r => _byRecord[t][r] > 0)
                .OrderByDescending(r => _byRecord[t][r])
                .Select(r => $"{r + schema.FirstId:X2} {schema.NameOf(r + schema.FirstId)} x{_byRecord[t][r]}");
            text.AppendLine($"  records read (most first): {string.Join("; ", busiest)}");
            // And which code read each record, for the records read least (the ones a list may skip).
            foreach (var r in Enumerable.Range(0, schema.RecordCount).Where(r => _byRecord[t][r] > 0 && _byRecord[t][r] < 50))
                text.AppendLine($"    {r + schema.FirstId:X2} {schema.NameOf(r + schema.FirstId)}: x{_byRecord[t][r]}");
        }
        if (_dropped > 0)
            text.AppendLine($"(code-address table full; {_dropped} reads not attributed)");
        File.WriteAllText(path, text.ToString());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_POINTERS
    {
        public byte* ExceptionRecord;
        public byte* ContextRecord;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public void* BaseAddress;
        public void* AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll")]
    private static extern void* AddVectoredExceptionHandler(uint first, delegate* unmanaged<EXCEPTION_POINTERS*, int> handler);

    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(void* address, MEMORY_BASIC_INFORMATION* info, nuint length);

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(void* address, nuint size, uint protect, out uint old);
}
