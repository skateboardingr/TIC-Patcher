using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

// TICScan: finds the job table in the running game, read-only.
//   TICScan <reference Jobs.bin> [process name or id, default fft_enhanced] [--all]
// The reference is a copy of the original job table (e.g. FFTPatcher's
// PSP defaults); it is only compared against, never written anywhere.
// Without --all only the game's own executable image is searched.

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("TICScan reads another process's memory and needs Windows.");
    return 2;
}
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: TICScan <reference Jobs.bin> [process name or id] [--all]");
    return 2;
}

var reference = File.ReadAllBytes(args[0]);
var schema = Jobs.Schema;
if (reference.Length != schema.RecordSize * schema.RecordCount)
{
    Console.Error.WriteLine($"Expected {schema.RecordSize * schema.RecordCount} bytes of reference, got {reference.Length}.");
    return 2;
}
var processName = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? "fft_enhanced";
var all = args.Contains("--all");

// --set <job id> <field> <value>: write one field of one job in the running
// game (temporary: gone when the game closes). Values as in RecordCodec.
var setIndex = Array.IndexOf(args, "--set");
(int Job, string Field, string Value)? set = setIndex >= 0 && args.Length > setIndex + 3
    ? (Convert.ToInt32(args[setIndex + 1], 16), args[setIndex + 2], args[setIndex + 3])
    : null;
if (set is not null)
    processName = args.Skip(1).Take(setIndex - 1).FirstOrDefault(a => !a.StartsWith("--")) ?? "fft_enhanced";

var process = int.TryParse(processName, out var pid)
    ? Process.GetProcessById(pid)
    : Process.GetProcessesByName(processName).FirstOrDefault();
if (process is null)
{
    Console.Error.WriteLine($"{processName} isn't running.");
    return 1;
}
return Scanner.Run(process, reference, all, set);

[SupportedOSPlatform("windows")]
static class Scanner
{
    public static int Run(Process process, byte[] reference, bool all, (int Job, string Field, string Value)? set)
    {
        var module = process.MainModule ?? throw new InvalidOperationException("no main module");
        var moduleStart = (long)module.BaseAddress;
        var moduleEnd = moduleStart + module.ModuleMemorySize;
        Console.WriteLine($"{process.ProcessName} pid {process.Id}: {module.ModuleName} at 0x{moduleStart:X}, {module.ModuleMemorySize / 1024 / 1024} MB");

        var access = Native.PROCESS_QUERY_INFORMATION | Native.PROCESS_VM_READ
            | (set is null ? 0 : Native.PROCESS_VM_WRITE | Native.PROCESS_VM_OPERATION);
        var handle = Native.OpenProcess(access, false, process.Id);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess");
        try
        {
            var locator = TableLocator.ForJobs(reference);
            var found = 0;
            foreach (var (start, bytes) in ContiguousBlocks(handle, all ? 0 : moduleStart, all ? long.MaxValue : moduleEnd))
            {
                foreach (var match in locator.Find(bytes).Take(5))
                {
                    found++;
                    var address = start + match.Offset;
                    var where = address >= moduleStart && address < moduleEnd ? $" (module +0x{address - moduleStart:X})" : "";
                    Console.WriteLine($"  candidate 0x{address:X}{where}: record size {match.RecordSize}, score {match.Score:P1}");
                    Describe(bytes, match, Jobs.FirstGenericJob, "Squire");
                    Describe(bytes, match, Jobs.FirstGenericJob + 1, "Chemist");
                    for (var ramza = 1; ramza <= 3; ramza++)
                        Describe(bytes, match, ramza, $"Ramza {ramza}");
                    if (set is { } s && found == 1)
                        Write(handle, address, bytes, match, s);
                }
            }
            Console.WriteLine(found == 0 ? "No candidates. Try --all." : $"{found} candidate(s).");
            return found == 0 ? 1 : 0;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    // Writes one field of one job record in the running game. The table may be
    // in read-only memory, so the page is made writable for the write only.
    private static void Write(IntPtr handle, long tableAddress, byte[] bytes, LocatorMatch match, (int Job, string Field, string Value) set)
    {
        var field = Jobs.Schema.Field(set.Field);
        var recordOffset = (int)(match.Offset + (long)set.Job * match.RecordSize);
        var record = bytes.AsSpan(recordOffset, Jobs.Schema.RecordSize).ToArray();
        var before = RecordCodec.Read(field, record);
        RecordCodec.Write(field, record, set.Value);

        var target = (IntPtr)(tableAddress + (long)set.Job * match.RecordSize + field.Offset);
        var size = (IntPtr)field.Size;
        if (!Native.VirtualProtectEx(handle, target, size, Native.PAGE_READWRITE, out var oldProtect))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualProtectEx");
        try
        {
            if (!Native.WriteProcessMemory(handle, target, record[field.Offset..(field.Offset + field.Size)], size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory");
        }
        finally
        {
            Native.VirtualProtectEx(handle, target, size, oldProtect, out _);
        }
        Console.WriteLine($"  set job 0x{set.Job:X2} {set.Field}: {before} -> {RecordCodec.Read(field, record)} (at 0x{(long)target:X})");
    }

    // A found record decoded with the reference layout, to eyeball against the game.
    private static void Describe(byte[] bytes, LocatorMatch match, int job, string label)
    {
        var record = bytes.AsSpan((int)(match.Offset + (long)job * match.RecordSize), Jobs.Schema.RecordSize).ToArray();
        var fields = new[] { "HPConstant", "HPMultiplier", "PAConstant", "PAMultiplier", "Move", "Jump", "CEvade", "Equipment" };
        Console.WriteLine($"    {label} (0x{job:X2}): " + string.Join(", ", fields.Select(f => $"{f}={RecordCodec.Read(Jobs.Schema.Field(f), record)}")));
    }

    // Back-to-back readable regions joined into one block: changing a page's
    // protection (as a write does) splits a region, and a table must not be
    // missed because it now straddles the split.
    private static IEnumerable<(long Start, byte[] Bytes)> ContiguousBlocks(IntPtr handle, long from, long to)
    {
        long blockStart = -1;
        var parts = new List<byte[]>();
        long blockEnd = -1;
        foreach (var (start, bytes) in ReadableRegions(handle, from, to))
        {
            if (parts.Count > 0 && start != blockEnd)
            {
                yield return (blockStart, Join(parts));
                parts.Clear();
            }
            if (parts.Count == 0)
                blockStart = start;
            parts.Add(bytes);
            blockEnd = start + bytes.Length;
        }
        if (parts.Count > 0)
            yield return (blockStart, Join(parts));
    }

    private static byte[] Join(List<byte[]> parts)
    {
        if (parts.Count == 1)
            return parts[0];
        var joined = new byte[parts.Sum(p => (long)p.Length)];
        long at = 0;
        foreach (var part in parts)
        {
            part.CopyTo(joined, at);
            at += part.Length;
        }
        return joined;
    }

    private static IEnumerable<(long Start, byte[] Bytes)> ReadableRegions(IntPtr handle, long from, long to)
    {
        long address = from;
        while (address < to && Native.VirtualQueryEx(handle, (IntPtr)address, out var info, (IntPtr)Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION>()) != IntPtr.Zero)
        {
            long regionStart = (long)info.BaseAddress;
            long regionSize = (long)info.RegionSize;
            address = regionStart + regionSize;
            if (info.State != Native.MEM_COMMIT || (info.Protect & Native.PAGE_GUARD) != 0 || (info.Protect & Native.READABLE) == 0)
                continue;
            if (regionSize > 1L << 30)
                continue; // skip giant reservations
            var buffer = new byte[regionSize];
            if (!Native.ReadProcessMemory(handle, (IntPtr)regionStart, buffer, (IntPtr)regionSize, out var read) || (long)read == 0)
                continue;
            if ((long)read < regionSize)
                Array.Resize(ref buffer, (int)(long)read);
            yield return (regionStart, buffer);
        }
    }
}

[SupportedOSPlatform("windows")]
static class Native
{
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_VM_WRITE = 0x0020;
    public const uint PROCESS_VM_OPERATION = 0x0008;
    public const uint PAGE_READWRITE = 0x04;
    public const uint MEM_COMMIT = 0x1000;
    public const uint PAGE_GUARD = 0x100;
    // PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY
    public const uint READABLE = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MEMORY_BASIC_INFORMATION info, IntPtr length);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualProtectEx(IntPtr process, IntPtr address, IntPtr size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);
}
