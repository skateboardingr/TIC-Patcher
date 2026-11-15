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

var process = int.TryParse(processName, out var pid)
    ? Process.GetProcessById(pid)
    : Process.GetProcessesByName(processName).FirstOrDefault();
if (process is null)
{
    Console.Error.WriteLine($"{processName} isn't running.");
    return 1;
}
return Scanner.Run(process, reference, all);

[SupportedOSPlatform("windows")]
static class Scanner
{
    public static int Run(Process process, byte[] reference, bool all)
    {
        var module = process.MainModule ?? throw new InvalidOperationException("no main module");
        var moduleStart = (long)module.BaseAddress;
        var moduleEnd = moduleStart + module.ModuleMemorySize;
        Console.WriteLine($"{process.ProcessName} pid {process.Id}: {module.ModuleName} at 0x{moduleStart:X}, {module.ModuleMemorySize / 1024 / 1024} MB");

        var handle = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION | Native.PROCESS_VM_READ, false, process.Id);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess");
        try
        {
            var locator = TableLocator.ForJobs(reference);
            var found = 0;
            foreach (var (start, bytes) in ReadableRegions(handle, all ? 0 : moduleStart, all ? long.MaxValue : moduleEnd))
            {
                foreach (var match in locator.Find(bytes).Take(5))
                {
                    found++;
                    var address = start + match.Offset;
                    var where = address >= moduleStart && address < moduleEnd ? $" (module +0x{address - moduleStart:X})" : "";
                    Console.WriteLine($"  candidate 0x{address:X}{where}: record size {match.RecordSize}, score {match.Score:P1}");
                    Describe(bytes, match, Jobs.FirstGenericJob, "Squire");
                    Describe(bytes, match, Jobs.FirstGenericJob + 1, "Chemist");
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

    // A found record decoded with the reference layout, to eyeball against the game.
    private static void Describe(byte[] bytes, LocatorMatch match, int job, string label)
    {
        var record = bytes.AsSpan((int)(match.Offset + (long)job * match.RecordSize), Jobs.Schema.RecordSize).ToArray();
        var fields = new[] { "HPConstant", "HPMultiplier", "PAConstant", "PAMultiplier", "Move", "Jump", "CEvade", "Equipment" };
        Console.WriteLine($"    {label} (0x{job:X2}): " + string.Join(", ", fields.Select(f => $"{f}={RecordCodec.Read(Jobs.Schema.Field(f), record)}")));
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
    public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);
}
