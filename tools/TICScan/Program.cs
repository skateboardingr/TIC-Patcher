using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

// TICScan: finds the job table in the running game, read-only.
//   TICScan <reference Jobs.bin> [process name or id, default fft_enhanced] [--all]
//           [--set <job id hex> <field> <value>]
// The reference is a copy of the original job table (e.g. FFTPatcher's
// PSP defaults); it is only compared against, never written anywhere.
// Without --all only the game's own executable image is searched.
// --set writes one field of one job (temporary: gone when the game closes).
// --dump <file> also saves the game's executable image as it is in memory,
// unreadable gaps zero-filled, so offsets in the file are module offsets.
// It's for offline research only; never share it (it is the game's code).

// TICScan --image <image.bin> <table> <id>... prints records, all fields,
// from a --dump file of a known game version. Works on any OS.
if (args.Length >= 4 && args[0] == "--image")
    return ImageViewer.Show(args[1], args[2], args[3..]);

// TICScan --save-vanilla <FFT_enhanced.exe or memory image> <out.json>, and
// TICScan --compare <a.json> <b.json>: research helpers for the original tables.
if (args.Length >= 3 && args[0] == "--save-vanilla")
{
    var bytes = File.ReadAllBytes(args[1]);
    var looksLikeImage = bytes.Length > 0x1000 && BitConverter.ToUInt32(bytes, BitConverter.ToInt32(bytes, 0x3C) + 24 + 56) == bytes.Length;
    var data = TICPatcher.Core.Mods.VanillaData.FromImage(looksLikeImage ? bytes : TICPatcher.Core.Mods.VanillaData.ImageOf(bytes));
    data.Save(args[2]);
    Console.WriteLine($"saved {data.Tables.Count} tables from {(looksLikeImage ? "a memory image" : "an executable")}");
    return 0;
}
if (args.Length >= 3 && args[0] == "--compare")
{
    var a = TICPatcher.Core.Mods.VanillaData.Load(args[1]);
    var b = TICPatcher.Core.Mods.VanillaData.Load(args[2]);
    foreach (var (name, bytes) in a.Tables)
    {
        var other = b.Tables[name];
        var differ = Enumerable.Range(0, bytes.Length).Where(i => bytes[i] != other[i]).ToList();
        var size = AllTables.ByName[name].RecordSize;
        Console.WriteLine($"  {name,-18} {(differ.Count == 0 ? "identical" : $"{differ.Count} byte(s) differ, records {string.Join(" ", differ.Select(i => $"{i / size + AllTables.ByName[name].FirstId:X2}").Distinct().Take(10))}")}");
    }
    return 0;
}

// TICScan --exe <FFT_enhanced.exe>: can the original tables be read from the file on disk?
if (args.Length >= 2 && args[0] == "--exe")
{
    try
    {
        var vanilla = TICPatcher.Core.Mods.VanillaData.FromExecutable(args[1]);
        var squire = vanilla.Tables["Jobs"].AsSpan(0x4A * 49, 49).ToArray();
        Console.WriteLine($"Read from the file: {vanilla.BuildName}, {vanilla.Tables.Count} tables. Squire Move={RecordCodec.Read(Jobs.Schema.Field("Move"), squire)}, HPConstant={RecordCodec.Read(Jobs.Schema.Field("HPConstant"), squire)}");
        return 0;
    }
    catch (Exception error) when (error is FormatException or NotSupportedException or InvalidOperationException or IOException)
    {
        Console.WriteLine($"Can't read the tables from the file: {error.Message}");
        return 1;
    }
}

// TICScan --copies <table> [record id hex] [bytes]: finds copies of a table elsewhere
// in the running game's memory (e.g. one the game builds at run time), by its first
// bytes; with a record id, finds copies of just that record's first <bytes> bytes.
if (args.Length >= 2 && args[0] == "--copies")
{
    if (!OperatingSystem.IsWindows())
        return 2;
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    int? record = args.Length > 2 ? Convert.ToInt32(args[2], 16) : null;
    var length = args.Length > 3 ? int.Parse(args[3]) : 0;
    return game is null ? 1 : Copies.Find(game, args[1], record, length);
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("TICScan reads another process's memory and needs Windows.");
    return 2;
}
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: TICScan <reference Jobs.bin> [process name or id] [--all] [--set <job> <field> <value>]");
    return 2;
}

var reference = File.ReadAllBytes(args[0]);
var schema = Jobs.Schema;
if (reference.Length != schema.RecordSize * schema.RecordCount)
{
    Console.Error.WriteLine($"Expected {schema.RecordSize * schema.RecordCount} bytes of reference, got {reference.Length}.");
    return 2;
}

var setIndex = Array.IndexOf(args, "--set");
(int Job, string Field, string Value)? set = setIndex >= 0 && args.Length > setIndex + 3
    ? (Convert.ToInt32(args[setIndex + 1], 16), args[setIndex + 2], args[setIndex + 3])
    : null;
var dumpIndex = Array.IndexOf(args, "--dump");
var dump = dumpIndex >= 0 && args.Length > dumpIndex + 1 ? args[dumpIndex + 1] : null;
var positional = args.Skip(1).Take(setIndex >= 0 ? setIndex - 1 : int.MaxValue).Where(a => !a.StartsWith("--") && a != dump);
var processName = positional.FirstOrDefault() ?? "fft_enhanced";
var all = args.Contains("--all");

var process = int.TryParse(processName, out var pid)
    ? Process.GetProcessById(pid)
    : Process.GetProcessesByName(processName).FirstOrDefault();
if (process is null)
{
    Console.Error.WriteLine($"{processName} isn't running.");
    return 1;
}
return Scanner.Run(process, reference, all, set, dump);

[SupportedOSPlatform("windows")]
static class Scanner
{
    public static int Run(Process process, byte[] reference, bool all, (int Job, string Field, string Value)? set, string? dump)
    {
        var module = process.MainModule ?? throw new InvalidOperationException("no main module");
        var moduleStart = (long)module.BaseAddress;
        var moduleEnd = moduleStart + module.ModuleMemorySize;
        Console.WriteLine($"{process.ProcessName} pid {process.Id}: {module.ModuleName} at 0x{moduleStart:X}, {module.ModuleMemorySize / 1024 / 1024} MB");

        var access = ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ
            | (set is null ? 0 : ProcessMemory.Native.PROCESS_VM_WRITE | ProcessMemory.Native.PROCESS_VM_OPERATION);
        var handle = ProcessMemory.Native.OpenProcess(access, false, process.Id);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess");
        try
        {
            var memory = new ProcessMemory(handle);
            if (dump is not null)
            {
                var image = new byte[module.ModuleMemorySize];
                foreach (var (start, bytes) in memory.ContiguousBlocks(moduleStart, moduleEnd))
                    bytes.AsSpan(0, (int)Math.Min(bytes.Length, moduleEnd - start)).CopyTo(image.AsSpan((int)(start - moduleStart)));
                File.WriteAllBytes(dump, image);
                Console.WriteLine($"  saved {image.Length / 1024 / 1024} MB to {dump}");
            }
            var locator = TableLocator.ForJobs(reference);
            var found = 0;
            foreach (var (start, bytes) in memory.ContiguousBlocks(all ? 0 : moduleStart, all ? long.MaxValue : moduleEnd))
            {
                foreach (var match in locator.Find(bytes).Take(5))
                {
                    found++;
                    var address = start + match.Offset;
                    var where = address >= moduleStart && address < moduleEnd ? $" (module +0x{address - moduleStart:X})" : "";
                    Console.WriteLine($"  candidate 0x{address:X}{where}: record size {match.RecordSize}, score {match.Score:P1}");
                    Describe(bytes, match, Jobs.FirstGenericJob, "Squire");
                    Describe(bytes, match, Jobs.FirstGenericJob + 1, "Chemist");
                    var skillSets = Enumerable.Range(Jobs.FirstGenericJob, Jobs.LastGenericJob - Jobs.FirstGenericJob + 1)
                        .Select(j => bytes[match.Offset + (long)j * match.RecordSize]);
                    Console.WriteLine($"    generic jobs' skill sets: {string.Join(" ", skillSets)}");
                    for (var ramza = 1; ramza <= 3; ramza++)
                        Describe(bytes, match, ramza, $"Ramza {ramza}");
                    if (set is { } s && found == 1)
                        Write(memory, address, bytes, match, s);
                }
            }
            Console.WriteLine(found == 0 ? "No candidates. Try --all." : $"{found} candidate(s).");
            return found == 0 ? 1 : 0;
        }
        finally
        {
            ProcessMemory.Native.CloseHandle(handle);
        }
    }

    private static void Write(ProcessMemory memory, long tableAddress, byte[] bytes, LocatorMatch match, (int Job, string Field, string Value) set)
    {
        var field = Jobs.Schema.Field(set.Field);
        var recordOffset = (int)(match.Offset + (long)set.Job * match.RecordSize);
        var record = bytes.AsSpan(recordOffset, Jobs.Schema.RecordSize).ToArray();
        var before = RecordCodec.Read(field, record);
        RecordCodec.Write(field, record, set.Value);
        var target = tableAddress + (long)set.Job * match.RecordSize + field.Offset;
        memory.Write(target, record[field.Offset..(field.Offset + field.Size)]);
        Console.WriteLine($"  set job 0x{set.Job:X2} {set.Field}: {before} -> {RecordCodec.Read(field, record)} (at 0x{target:X})");
    }

    // A found record decoded with the reference layout, to eyeball against the game.
    private static void Describe(byte[] bytes, LocatorMatch match, int job, string label)
    {
        var record = bytes.AsSpan((int)(match.Offset + (long)job * match.RecordSize), Jobs.Schema.RecordSize).ToArray();
        var fields = new[] { "HPConstant", "HPMultiplier", "PAConstant", "PAMultiplier", "Move", "Jump", "CEvade", "Equipment" };
        Console.WriteLine($"    {label} (0x{job:X2}): " + string.Join(", ", fields.Select(f => $"{f}={RecordCodec.Read(Jobs.Schema.Field(f), record)}")));
    }
}

static class ImageViewer
{
    public static int Show(string path, string table, string[] ids)
    {
        var image = File.ReadAllBytes(path);
        var build = GameBuilds.Identify(image);
        if (build is null)
        {
            Console.Error.WriteLine("Unknown game version.");
            return 1;
        }
        if (!AllTables.ByName.TryGetValue(table, out var schema) || !build.Tables.TryGetValue(table, out var offset))
        {
            Console.Error.WriteLine($"Unknown table {table}. Tables: {string.Join(", ", build.Tables.Keys)}");
            return 2;
        }
        Console.WriteLine($"{build.Name}: {table} at +0x{offset:X}");
        foreach (var text in ids)
        {
            var id = Convert.ToInt32(text, 16);
            var record = image.AsSpan((int)(offset + (long)(id - schema.FirstId) * schema.RecordSize), schema.RecordSize).ToArray();
            Console.WriteLine($"  0x{id:X2}: " + string.Join("; ", schema.Fields.Select(f => $"{f.Name}={RecordCodec.Read(f, record)}")));
        }
        return 0;
    }
}

[SupportedOSPlatform("windows")]
static class Copies
{
    public static int Find(Process game, string table, int? record = null, int length = 0)
    {
        var module = game.MainModule!;
        var start = (long)module.BaseAddress;
        var end = start + module.ModuleMemorySize;
        var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
        try
        {
            var memory = new ProcessMemory(handle);
            var build = GameBuilds.Identify(memory.Read(start, 0x1000)) ?? throw new NotSupportedException("unknown game version");
            var schema = AllTables.ByName[table];
            var original = memory.Read(start + build.Tables[table], schema.Length);
            // Skip the first records (often blank); look for a distinctive run.
            var probe = original.AsSpan(schema.RecordSize * 1, Math.Min(96, schema.Length - schema.RecordSize)).ToArray();
            if (record is { } id)
            {
                probe = original.AsSpan((id - schema.FirstId) * schema.RecordSize, length > 0 ? length : schema.RecordSize).ToArray();
                Console.WriteLine($"record 0x{id:X2}, first {probe.Length} bytes: {Convert.ToHexString(probe)}");
            }
            Console.WriteLine($"{table}: original at module +0x{build.Tables[table]:X}; searching all memory for copies");
            var found = 0;
            foreach (var (blockStart, bytes) in memory.ContiguousBlocks(0, long.MaxValue))
            {
                var span = bytes.AsSpan();
                for (var at = span.IndexOf(probe); at >= 0; )
                {
                    var copy = blockStart + at - schema.RecordSize;
                    var where = copy >= start && copy < end ? $"module +0x{copy - start:X}" : "outside the module";
                    var same = at - schema.RecordSize >= 0 && at - schema.RecordSize + schema.Length <= span.Length
                        ? span.Slice(at - schema.RecordSize, schema.Length).SequenceEqual(original) ? "identical" : "differs further on"
                        : "cut off";
                    Console.WriteLine($"  0x{copy:X} ({where}), {same}");
                    found++;
                    var next = span[(at + 1)..].IndexOf(probe);
                    at = next < 0 ? -1 : at + 1 + next;
                }
            }
            Console.WriteLine($"{found} place(s).");
            return 0;
        }
        finally
        {
            ProcessMemory.Native.CloseHandle(handle);
        }
    }
}
