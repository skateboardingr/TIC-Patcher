namespace TICPatcher.Core.Mods;

/// <summary>
/// Edits a data file as the game unpacks it. The game decompresses its archives in pieces
/// (e.g. 512 KB) straight into the buffers it then uses, so a file can start partway through
/// one piece and run on into the next. Given each piece as it comes out, this finds which part
/// of the file it holds (by distinctive records of the original) and replaces every record
/// there that still holds its original bytes with the mod's version.
/// Measured on build 24304444: the battle files arrive 0x6B000 into a 0x80000-byte piece.
/// </summary>
public sealed class LoadPatcher
{
    private readonly byte[] _original;
    private readonly byte[] _wanted;
    private readonly int _recordSize;
    private readonly List<(int Offset, byte[] Bytes)> _markers = [];

    /// <param name="original">The file as the game ships it.</param>
    /// <param name="wanted">The file as the mods want it (same length).</param>
    /// <param name="recordSize">Records are replaced whole (e.g. a 40-byte battle unit).</param>
    public LoadPatcher(byte[] original, byte[] wanted, int recordSize)
    {
        if (original.Length != wanted.Length)
            throw new ArgumentException("The original and wanted files differ in length.");
        _original = original;
        _wanted = wanted;
        _recordSize = recordSize;
        // Markers: a distinctive record (not blank, found once in the file) in every 16 KB stretch,
        // so any piece holding part of the file contains at least one of them.
        var counts = new Dictionary<string, int>();
        for (var at = 0; at + recordSize <= original.Length; at += recordSize)
        {
            var key = Convert.ToHexString(original, at, recordSize);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        const int Stretch = 0x4000;
        for (var from = 0; from < original.Length; from += Stretch)
            for (var at = (from + recordSize - 1) / recordSize * recordSize; at < Math.Min(from + Stretch, original.Length) && at + recordSize <= original.Length; at += recordSize)
            {
                var record = original.AsSpan(at, recordSize);
                if (record.ContainsAnyExcept((byte)0) && counts[Convert.ToHexString(record)] == 1)
                {
                    _markers.Add((at, record.ToArray()));
                    break;
                }
            }
    }

    /// <summary>Whether the mods change this file at all.</summary>
    public bool HasChanges => !_original.AsSpan().SequenceEqual(_wanted);

    /// <summary>
    /// Edits a freshly unpacked piece in place if it holds any part of the file. Returns the
    /// records replaced.
    /// </summary>
    public int Patch(Span<byte> piece)
    {
        // Where the file starts relative to the piece (before it, if the file began in an earlier
        // piece), from the first marker found. A coincidental match elsewhere does no harm: only
        // bytes still equal to the original are replaced.
        long? fileStart = null;
        foreach (var (offset, bytes) in _markers)
        {
            var at = piece.IndexOf(bytes);
            if (at >= 0)
            {
                fileStart = at - (long)offset;
                break;
            }
        }
        if (fileStart is not { } start)
            return 0;
        // Every record overlapping the piece, whole or the part of it the piece holds (a record can
        // be split between two pieces).
        var replaced = 0;
        var firstByte = Math.Max(0, -start);
        var lastByte = Math.Min(_original.Length, piece.Length - start);
        for (var recordAt = firstByte / _recordSize * _recordSize; recordAt < lastByte; recordAt += _recordSize)
        {
            var from = Math.Max(recordAt, firstByte);
            var to = Math.Min(recordAt + _recordSize, lastByte);
            if (to <= from)
                continue;
            var here = piece.Slice((int)(start + from), (int)(to - from));
            var wanted = _wanted.AsSpan((int)from, (int)(to - from));
            if (here.SequenceEqual(wanted) || !here.SequenceEqual(_original.AsSpan((int)from, (int)(to - from))))
                continue; // already as wanted, or not the original (left alone)
            wanted.CopyTo(here);
            replaced++;
        }
        return replaced;
    }
}
