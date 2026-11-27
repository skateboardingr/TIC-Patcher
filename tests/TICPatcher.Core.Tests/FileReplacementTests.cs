using TICPatcher.Core.Archives;

namespace TICPatcher.Core.Tests;

// Generated data only: a made-up block unpacked in pieces, as the game does.
public class FileReplacementTests
{
    private static (byte[] Block, BlockFileReplacement Replacement) Make(int replacementSize)
    {
        var random = new Random(11);
        var block = new byte[0x50000];
        random.NextBytes(block);
        var replacement = new byte[replacementSize];
        random.NextBytes(replacement);
        return (block, new BlockFileReplacement("0004", "nxd/test.nxd", 0x1122334455667788, block, 0x1F000, 0x3000, replacement));
    }

    [Fact]
    public void FindsTheBlockFromAnyPieceAndPatchesTheFileInPlace()
    {
        var (block, replacement) = Make(0x3000);
        var memory = (byte[])block.Clone();
        const long At = 0x7000_0000;
        var written = 0;
        // Pieces of 64 KB, the file straddling two of them.
        for (var from = 0; from < memory.Length; from += 0x10000)
        {
            var piece = memory.AsSpan(from, 0x10000);
            Assert.Equal(At, replacement.BlockStart(piece, At + from));
            written += replacement.PatchInPlace(piece, At + from, At);
        }
        Assert.Equal(0x3000, written);
        Assert.Equal(replacement.Replacement, memory.AsSpan(0x1F000, 0x3000).ToArray());
        Assert.Equal(block.AsSpan(0, 0x1F000).ToArray(), memory.AsSpan(0, 0x1F000).ToArray());
        // Something else entirely isn't taken for the block.
        var other = new byte[0x10000];
        new Random(3).NextBytes(other);
        Assert.Null(replacement.BlockStart(other, 0x5000));
    }

    [Fact]
    public void RepointsTheGamesEntryForAnotherSize()
    {
        var (_, replacement) = Make(0x3400);
        Assert.False(replacement.SameSize);
        var entry = new byte[0x38];
        BitConverter.TryWriteBytes(entry.AsSpan(0x00), (uint)0x3000);
        BitConverter.TryWriteBytes(entry.AsSpan(0x08), 0x3000L);
        BitConverter.TryWriteBytes(entry.AsSpan(0x10), 0x1F000L);
        BitConverter.TryWriteBytes(entry.AsSpan(0x28), 0x1122334455667788UL);
        Assert.True(replacement.IsEntry(entry));

        BlockFileReplacement.Repoint(entry, 0x7000_0000, 0x2_0000_0000, 0x3400);
        Assert.Equal(0x3400L, BitConverter.ToInt64(entry, 0x08));
        Assert.Equal(0x2_0000_0000L - 0x7000_0000, BitConverter.ToInt64(entry, 0x10));
        Assert.False(replacement.IsEntry(entry)); // no longer the original
    }
}
