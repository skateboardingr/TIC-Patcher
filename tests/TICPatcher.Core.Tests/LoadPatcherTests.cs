using TICPatcher.Core.Mods;

namespace TICPatcher.Core.Tests;

// Generated data only: a made-up "battle file" unpacked in pieces, as the game does.
public class LoadPatcherTests
{
    [Fact]
    public void EditsAFileSplitAcrossPieces()
    {
        const int Record = 40, FileSize = 0x50000, PieceSize = 0x80000, FileAt = 0x6B000;
        var random = new Random(7);
        var original = new byte[FileSize];
        random.NextBytes(original);
        var wanted = (byte[])original.Clone();
        // Edits near the start, in the middle, at the end, and in the record cut by the piece boundary.
        var straddling = (PieceSize - FileAt) / Record * Record;
        foreach (var at in new[] { 0, 0x20000 / Record * Record, FileSize - Record, straddling })
            wanted.AsSpan(at, Record).Fill(0xAB);
        Assert.True(straddling + Record > PieceSize - FileAt); // really split

        var memory = new byte[2 * PieceSize];
        random.NextBytes(memory);
        original.CopyTo(memory, FileAt);
        var patcher = new LoadPatcher(original, wanted, Record);
        var replaced = patcher.Patch(memory.AsSpan(0, PieceSize)) + patcher.Patch(memory.AsSpan(PieceSize, PieceSize));

        Assert.Equal(wanted, memory.AsSpan(FileAt, FileSize).ToArray());
        Assert.Equal(5, replaced); // the split record counts once in each piece
        // A piece with nothing of the file is left alone, and patching again changes nothing.
        var other = new byte[PieceSize];
        random.NextBytes(other);
        var copy = (byte[])other.Clone();
        Assert.Equal(0, patcher.Patch(other));
        Assert.Equal(copy, other);
        Assert.Equal(0, patcher.Patch(memory.AsSpan(0, PieceSize)));
    }
}
