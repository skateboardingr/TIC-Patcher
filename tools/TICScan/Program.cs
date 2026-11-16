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
