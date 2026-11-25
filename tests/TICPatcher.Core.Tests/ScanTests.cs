using TICPatcher.Core.Memory;

namespace TICPatcher.Core.Tests;

// Generated data only: a made-up pattern in this test's own memory.
public class ScanTests
{
    [Fact]
    public void ScanBlocksFindsWhatContiguousBlocksFinds()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // A pattern bigger than a page, at a known place, in its own allocation.
        var pattern = new byte[20000];
        new Random(5).NextBytes(pattern);
        var holder = GC.AllocateUninitializedArray<byte>(1 << 20, pinned: true);
        pattern.CopyTo(holder, 12345);
        var address = (long)System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(holder, 12345);
        var probe = pattern.AsSpan(0, 64).ToArray();

        var found = new List<long>();
        long biggest = 0;
        foreach (var (start, block) in ProcessMemory.Current.ScanBlocks(0, 0x7FFF_FFFF_FFFF, privateOnly: true))
        {
            biggest = Math.Max(biggest, block.Count);
            for (var at = block.AsSpan().IndexOf(probe); at >= 0;)
            {
                found.Add(start + at);
                var next = block.AsSpan(at + 1).IndexOf(probe);
                at = next < 0 ? -1 : at + 1 + next;
            }
        }
        Assert.Contains(address, found);
        Assert.True(biggest <= 64 << 20);
        GC.KeepAlive(holder);
    }
}
