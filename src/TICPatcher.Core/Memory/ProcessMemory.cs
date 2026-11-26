using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TICPatcher.Core.Memory;

/// <summary>
/// Reads and writes a Windows process's memory: another process (TICScan,
/// through a handle from OpenProcess) or the current one (TIC Loader, with
/// <see cref="Current"/>). Writes make only the touched pages writable, and
/// restore their protection afterwards.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessMemory(IntPtr handle)
{
    /// <summary>The current process (a pseudo-handle; nothing to close).</summary>
    public static ProcessMemory Current { get; } = new(new IntPtr(-1));

    public IntPtr Handle { get; } = handle;

    /// <summary>
    /// Readable memory between <paramref name="from"/> and <paramref name="to"/>,
    /// with back-to-back regions joined into one block: a protection change
    /// splits a region, and a table must not be missed because it straddles the split.
    /// </summary>
    /// <summary>
    /// Where this process's .NET managed heap is reserved (by allocation base), for code running
    /// inside the game: its own arrays and the buffers memory is read into live there, so a search
    /// of the game's memory skips hits in it rather than finding copies of what it read.
    /// </summary>
    public HashSet<long> ManagedHeapBases()
    {
        var bases = new HashSet<long>();
        foreach (var sample in new[] { new byte[16], new byte[200_000] })
        {
            var pin = GCHandle.Alloc(sample, GCHandleType.Pinned);
            try
            {
                bases.Add(AllocationBase((long)pin.AddrOfPinnedObject()));
            }
            finally
            {
                pin.Free();
            }
        }
        return bases;
    }

    /// <summary>The start of the reservation an address belongs to (0 if it isn't in one).</summary>
    public long AllocationBase(long address) =>
        Native.VirtualQueryEx(Handle, (IntPtr)address, out var info, (IntPtr)Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION>()) != IntPtr.Zero
            ? (long)info.AllocationBase
            : 0;

    public IEnumerable<(long Start, byte[] Bytes)> ContiguousBlocks(long from, long to, bool privateOnly = false)
    {
        // Joined runs are capped: inside the game, adjacent regions can add up to more
        // than an array holds. (Something straddling a cut is missed, which is rare.)
        const long MaxJoined = 256L << 20;
        long blockStart = -1;
        long blockEnd = -1;
        var parts = new List<byte[]>();
        foreach (var (start, bytes) in ReadableRegions(from, to, privateOnly))
        {
            if (parts.Count > 0 && (start != blockEnd || blockEnd - blockStart + bytes.Length > MaxJoined))
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

    /// <summary>
    /// Like <see cref="ContiguousBlocks"/>, but each block is read into one reused buffer, valid
    /// only until the next block: for scans of a whole process from inside the game, which would
    /// otherwise allocate (and leave .NET holding) gigabytes of short-lived arrays. Blocks are cut
    /// at 64 MB, so something straddling a cut is missed, which is rare.
    /// </summary>
    public IEnumerable<(long Start, ArraySegment<byte> Bytes)> ScanBlocks(long from, long to, bool privateOnly = false)
    {
        const int Max = 64 << 20;
        var buffer = _scanBuffer ??= GC.AllocateUninitializedArray<byte>(Max, pinned: true);
        // Scanning the current process, the buffer itself holds copies of what was just read:
        // its region is skipped, or a search would find them.
        var self = Handle == new IntPtr(-1) ? (long)Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0) : -1;
        long blockStart = 0;
        var used = 0;
        long address = from;
        while (address < to && Native.VirtualQueryEx(Handle, (IntPtr)address, out var info, (IntPtr)Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION>()) != IntPtr.Zero)
        {
            long regionStart = (long)info.BaseAddress;
            long regionSize = (long)info.RegionSize;
            address = regionStart + regionSize;
            if (info.State != Native.MEM_COMMIT || (info.Protect & Native.PAGE_GUARD) != 0 || (info.Protect & Native.READABLE) == 0
                || privateOnly && info.Type != Native.MEM_PRIVATE || (info.Protect & Native.UNCACHED) != 0 || regionSize > 1L << 30
                || self + Max > regionStart && self < regionStart + regionSize)
                continue;
            for (long offset = 0; offset < regionSize;)
            {
                // A block goes on only while memory is back to back and fits.
                if (used > 0 && (regionStart + offset != blockStart + used || used == Max))
                {
                    yield return (blockStart, new ArraySegment<byte>(buffer, 0, used));
                    used = 0;
                }
                if (used == 0)
                    blockStart = regionStart + offset;
                var piece = (int)Math.Min(regionSize - offset, Max - used);
                if (!Native.ReadProcessMemory(Handle, (IntPtr)(regionStart + offset), Marshal.UnsafeAddrOfPinnedArrayElement(buffer, used), (IntPtr)piece, out var read) || (long)read == 0)
                    break;
                used += (int)(long)read;
                offset += (long)read;
                if ((long)read < piece)
                    break;
            }
        }
        if (used > 0)
            yield return (blockStart, new ArraySegment<byte>(buffer, 0, used));
    }

    private byte[]? _scanBuffer;

    /// <param name="privateOnly">Only the process's own working memory (heap), skipping executables and mapped files.</param>
    public IEnumerable<(long Start, byte[] Bytes)> ReadableRegions(long from, long to, bool privateOnly = false)
    {
        long address = from;
        while (address < to && Native.VirtualQueryEx(Handle, (IntPtr)address, out var info, (IntPtr)Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION>()) != IntPtr.Zero)
        {
            long regionStart = (long)info.BaseAddress;
            long regionSize = (long)info.RegionSize;
            address = regionStart + regionSize;
            if (info.State != Native.MEM_COMMIT || (info.Protect & Native.PAGE_GUARD) != 0 || (info.Protect & Native.READABLE) == 0)
                continue;
            if (privateOnly && info.Type != Native.MEM_PRIVATE)
                continue;
            if ((info.Protect & Native.UNCACHED) != 0)
                continue; // GPU upload buffers and the like: very slow to read, and never game data
            if (regionSize > 1L << 30)
                continue; // skip giant reservations
            var buffer = new byte[regionSize];
            if (!Native.ReadProcessMemory(Handle, (IntPtr)regionStart, buffer, (IntPtr)regionSize, out var read) || (long)read == 0)
                continue;
            if ((long)read < regionSize)
                Array.Resize(ref buffer, (int)(long)read);
            yield return (regionStart, buffer);
        }
    }

    public byte[] Read(long address, int length)
    {
        var buffer = new byte[length];
        if (!Native.ReadProcessMemory(Handle, (IntPtr)address, buffer, (IntPtr)length, out var read) || (long)read != length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"ReadProcessMemory at 0x{address:X}");
        return buffer;
    }

    /// <summary>Writes bytes, making their pages writable only for the write.</summary>
    public void Write(long address, byte[] bytes)
    {
        var target = (IntPtr)address;
        var size = (IntPtr)bytes.Length;
        if (!Native.VirtualProtectEx(Handle, target, size, Native.PAGE_READWRITE, out var oldProtect))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualProtectEx");
        try
        {
            if (!Native.WriteProcessMemory(Handle, target, bytes, size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory");
        }
        finally
        {
            Native.VirtualProtectEx(Handle, target, size, oldProtect, out _);
        }
    }

    /// <summary>Allocates read-write memory wherever Windows chooses (0 if it can't).</summary>
    public long Allocate(int size) =>
        (long)Native.VirtualAllocEx(Handle, IntPtr.Zero, (IntPtr)size, Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);

    /// <summary>
    /// Allocates read-write memory at or above <paramref name="minAddress"/> and
    /// within <paramref name="within"/> bytes of it, or returns 0 if none is free there.
    /// </summary>
    public long Allocate(long minAddress, int size, long within = 0xF000_0000)
    {
        const long granularity = 0x10000;
        for (var hint = (minAddress + granularity - 1) & ~(granularity - 1); hint < minAddress + within; hint += granularity)
        {
            var at = Native.VirtualAllocEx(Handle, (IntPtr)hint, (IntPtr)size, Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
            if (at != IntPtr.Zero)
                return (long)at;
        }
        return 0;
    }

    /// <summary>Writes only the runs of bytes that differ between <paramref name="before"/> and <paramref name="after"/>.</summary>
    public int WriteChanges(long address, byte[] before, byte[] after)
    {
        var changed = 0;
        for (var i = 0; i < after.Length;)
        {
            if (before[i] == after[i])
            {
                i++;
                continue;
            }
            var start = i;
            while (i < after.Length && before[i] != after[i])
                i++;
            Write(address + start, after[start..i]);
            changed += i - start;
        }
        return changed;
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

    public static class Native
    {
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint MEM_PRIVATE = 0x20000;
        public const uint PAGE_GUARD = 0x100;
        public const uint PAGE_READWRITE = 0x04;
        // PAGE_NOCACHE | PAGE_WRITECOMBINE
        public const uint UNCACHED = 0x200 | 0x400;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, IntPtr buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, IntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualProtectEx(IntPtr process, IntPtr address, IntPtr size, uint newProtect, out uint oldProtect);
    }
}
