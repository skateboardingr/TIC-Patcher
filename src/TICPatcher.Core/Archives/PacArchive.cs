using System.Text;

namespace TICPatcher.Core.Archives;

/// <summary>
/// The game's .pac archives (data\enhanced\*.pac, data\classic\*.pac), worked
/// out from the files themselves (docs/archives.md):
/// <code>
/// 0x000  "PACK", u32 header size (data starts here), u32 file count, u8[4] flags, u64 data size
/// 0x018  archive name, 256 bytes, XOR'd with an 8-byte pattern
/// 0x118  u64 end of the entry table, u64 names offset, u64 names length
/// …      entries, 56 bytes each, ending at "end of the entry table"
/// …      block records, 24 bytes each (files packed together in one compressed block)
/// names  NUL-terminated file names, XOR'd with the same pattern
/// data   GDeflate streams, from "header size" on
/// </code>
/// </summary>
public sealed class PacArchive
{
    private static readonly byte[] Key = Convert.FromHexString("E612D6549A8F4C53");
    private const int EntrySize = 56;
    private const int BlockSize = 24;

    public string Path { get; }
    public string Name { get; }
    public uint HeaderSize { get; }
    public IReadOnlyList<PacEntry> Entries { get; }

    private readonly Dictionary<long, PacBlock> _blocks = [];

    private PacArchive(string path, string name, uint headerSize, IReadOnlyList<PacEntry> entries, Dictionary<long, PacBlock> blocks)
    {
        Path = path;
        Name = name;
        HeaderSize = headerSize;
        Entries = entries;
        _blocks = blocks;
    }

    public static PacArchive Open(string path)
    {
        using var file = File.OpenRead(path);
        var start = new byte[0x130];
        file.ReadExactly(start);
        if (Encoding.ASCII.GetString(start, 0, 4) != "PACK")
            throw new FormatException($"{path} isn't a .pac archive.");
        var headerSize = BitConverter.ToUInt32(start, 4);
        var header = new byte[headerSize];
        file.Position = 0;
        file.ReadExactly(header);
        return Parse(path, header);
    }

    public static PacArchive Parse(string path, byte[] header)
    {
        var headerSize = BitConverter.ToUInt32(header, 4);
        var count = BitConverter.ToInt32(header, 8);
        var name = CString(Unscramble(header.AsSpan(0x18, 0x100), 0x18), 0);
        var entriesEnd = (int)BitConverter.ToUInt64(header, 0x118);
        var namesAt = (int)BitConverter.ToUInt64(header, 0x120);
        var namesLength = (int)BitConverter.ToUInt64(header, 0x128);
        var names = Unscramble(header.AsSpan(namesAt, namesLength), namesAt);

        var blocks = new Dictionary<long, PacBlock>();
        var entries = new List<PacEntry>(count);
        var tableAt = entriesEnd - count * EntrySize;
        for (var i = 0; i < count; i++)
        {
            var e = header.AsSpan(tableAt + i * EntrySize, EntrySize);
            var compressedSize = BitConverter.ToUInt32(e);
            var compressed = e[4] != 0;
            var kind = e[5];
            var size = (long)BitConverter.ToUInt64(e[8..]);
            var offset = (long)BitConverter.ToUInt64(e[16..]);
            var block = (long)BitConverter.ToUInt64(e[24..]);
            var nameOffset = (int)(BitConverter.ToUInt64(e[32..]) - (ulong)namesAt);
            var hash = BitConverter.ToUInt64(e[40..]);
            if (block != 0 && !blocks.ContainsKey(block))
            {
                var b = header.AsSpan((int)block, BlockSize);
                blocks[block] = new PacBlock((long)BitConverter.ToUInt64(b), BitConverter.ToUInt32(b[8..]), BitConverter.ToUInt32(b[12..]));
            }
            entries.Add(new PacEntry(CString(names, nameOffset), size, compressedSize, compressed, kind, offset, block, hash));
        }
        return new PacArchive(path, name, headerSize, entries, blocks);
    }

    public PacEntry? Find(string name) => Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A file's contents, decompressed.</summary>
    /// <param name="wipe">Clear the other files decompressed with it before returning, e.g. so
    /// code running inside the game that later searches its memory for this file's contents
    /// doesn't find stray copies of its own.</param>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public byte[] Read(PacEntry entry, GDeflate codec, bool wipe = false)
    {
        using var file = File.OpenRead(Path);
        if (entry.Block != 0)
        {
            var block = _blocks[entry.Block];
            var packed = ReadAt(file, block.Offset, (int)block.CompressedSize);
            var unpacked = codec.Decompress(packed, (int)block.Size);
            var result = unpacked.AsSpan((int)entry.Offset, (int)entry.Size).ToArray();
            if (wipe)
                Array.Clear(unpacked);
            return result;
        }
        var data = ReadAt(file, entry.Offset, (int)entry.CompressedSize);
        if (!entry.Compressed)
            return data;
        var bytes = codec.Decompress(data, (int)entry.Size);
        return bytes;
    }

    private static byte[] ReadAt(FileStream file, long offset, int length)
    {
        var bytes = new byte[length];
        file.Position = offset;
        file.ReadExactly(bytes);
        return bytes;
    }

    private static byte[] Unscramble(ReadOnlySpan<byte> bytes, long fileOffset)
    {
        var plain = new byte[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            plain[i] = (byte)(bytes[i] ^ Key[(fileOffset + i) % 8]);
        return plain;
    }

    private static string CString(byte[] bytes, int offset)
    {
        var end = Array.IndexOf(bytes, (byte)0, offset);
        return Encoding.UTF8.GetString(bytes, offset, (end < 0 ? bytes.Length : end) - offset);
    }
}

/// <summary>
/// A file in a .pac. Stand-alone files are compressed on their own at
/// <see cref="Offset"/>; files in a <see cref="Block"/> sit at
/// <see cref="Offset"/> inside that block once it's decompressed.
/// </summary>
public sealed record PacEntry(string Name, long Size, uint CompressedSize, bool Compressed, byte Kind, long Offset, long Block, ulong Hash);

/// <summary>Several files compressed together: where, and how big before and after.</summary>
public sealed record PacBlock(long Offset, uint CompressedSize, uint Size);
