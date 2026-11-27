namespace TICPatcher.Core.Archives;

/// <summary>
/// A mod's replacement for a file the game unpacks as part of a block (several files compressed
/// together, e.g. every data table in 0004.pac). The game decompresses blocks in pieces, each
/// handed to TIC Loader's unpack hook; <see cref="BlockStart"/> works out where in memory the
/// whole block is from any piece of it, by distinctive bytes of the original block.
/// A replacement of the same size is copied into the block (<see cref="PatchInPlace"/>). A bigger
/// or smaller one is put in a buffer of its own, and the game's archive entry for the file is
/// pointed at it: its offset is a 64-bit distance from the block's start, so it can reach anywhere.
/// </summary>
public sealed class BlockFileReplacement
{
    private const int MarkerLength = 64;
    private const int MarkerSpacing = 0x8000;

    private readonly byte[] _block;
    private readonly List<(int Offset, byte[] Bytes)> _markers = [];

    /// <param name="block">The block as the game unpacks it (decompressed, unchanged).</param>
    /// <param name="fileOffset">Where the file starts in the block (the archive entry's offset).</param>
    /// <param name="fileSize">The file's size in the archive.</param>
    /// <param name="replacement">The file as the mod wants it.</param>
    public BlockFileReplacement(string archive, string path, ulong hash, byte[] block, long fileOffset, int fileSize, byte[] replacement)
    {
        if (fileOffset < 0 || fileOffset + fileSize > block.Length)
            throw new ArgumentException($"{path} isn't inside its block.");
        Archive = archive;
        Path = path;
        Hash = hash;
        _block = block;
        FileOffset = fileOffset;
        FileSize = fileSize;
        Replacement = replacement;
        // Distinctive stretches, found once in the block, every 32 KB, so any piece of the block
        // (the game's pieces are 64 KB or more) contains at least one.
        var seen = new Dictionary<string, int>();
        for (var at = 0; at + MarkerLength <= block.Length; at += MarkerLength)
        {
            var key = Convert.ToHexString(block, at, MarkerLength);
            seen[key] = seen.GetValueOrDefault(key) + 1;
        }
        for (var from = 0; from < block.Length; from += MarkerSpacing)
            for (var at = from / MarkerLength * MarkerLength; at < Math.Min(from + MarkerSpacing, block.Length) && at + MarkerLength <= block.Length; at += MarkerLength)
            {
                var bytes = block.AsSpan(at, MarkerLength);
                if (bytes.ToArray().Distinct().Count() > 8 && seen[Convert.ToHexString(bytes)] == 1)
                {
                    _markers.Add((at, bytes.ToArray()));
                    break;
                }
            }
    }

    public string Archive { get; }
    public string Path { get; }
    public ulong Hash { get; }
    public long FileOffset { get; }
    public int FileSize { get; }
    public byte[] Replacement { get; }

    public bool SameSize => Replacement.Length == FileSize;

    /// <summary>The block's start in memory, if <paramref name="piece"/> (at <paramref name="address"/>) is part of it.</summary>
    public long? BlockStart(ReadOnlySpan<byte> piece, long address)
    {
        foreach (var (offset, bytes) in _markers)
        {
            var at = piece.IndexOf(bytes);
            if (at < 0)
                continue;
            var start = address + at - offset;
            // The piece must match the block there throughout, not just at the marker.
            var inBlock = address - start;
            var length = (int)Math.Min(piece.Length, _block.Length - inBlock);
            if (inBlock >= 0 && length > 0 && piece[..length].SequenceEqual(_block.AsSpan((int)inBlock, length)))
                return start;
        }
        return null;
    }

    /// <summary>
    /// Copies the replacement (same size as the original) into the part of the file this piece
    /// holds; returns the number of bytes written.
    /// </summary>
    public int PatchInPlace(Span<byte> piece, long address, long blockStart)
    {
        if (!SameSize)
            throw new InvalidOperationException($"{Path} changes size; it can't be replaced in place.");
        var fileStart = blockStart + FileOffset;
        var from = Math.Max(address, fileStart);
        var to = Math.Min(address + piece.Length, fileStart + FileSize);
        if (from >= to)
            return 0;
        Replacement.AsSpan((int)(from - fileStart), (int)(to - from)).CopyTo(piece[(int)(from - address)..]);
        return (int)(to - from);
    }

    /// <summary>
    /// Whether <paramref name="entry"/> (the game's in-memory archive entry: u32 packed size, flags,
    /// u64 size, u64 offset, block, name, u64 hash) is this file's, unchanged.
    /// </summary>
    public bool IsEntry(ReadOnlySpan<byte> entry) =>
        entry.Length >= 0x30
        && BitConverter.ToUInt64(entry[0x28..]) == Hash
        && BitConverter.ToInt64(entry[0x08..]) == FileSize
        && BitConverter.ToInt64(entry[0x10..]) == FileOffset;

    /// <summary>The entry's bytes changed to point at a buffer holding the replacement.</summary>
    public static void Repoint(Span<byte> entry, long blockStart, long buffer, int size)
    {
        BitConverter.TryWriteBytes(entry[0x00..], (uint)size);
        BitConverter.TryWriteBytes(entry[0x08..], (long)size);
        BitConverter.TryWriteBytes(entry[0x10..], buffer - blockStart);
    }
}
