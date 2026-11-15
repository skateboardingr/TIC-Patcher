using TICPatcher.Core.Tables;

namespace TICPatcher.Core.Memory;

/// <summary>A place in memory that looks like the table, and how well it matches.</summary>
/// <param name="Offset">Where record 0 starts, from the start of the searched bytes.</param>
/// <param name="RecordSize">Distance between records there.</param>
/// <param name="Score">Share of the compared bytes that equal the reference (0..1).</param>
public sealed record LocatorMatch(long Offset, int RecordSize, double Score);

/// <summary>
/// Finds a table in a block of memory by comparing it with a reference copy
/// (e.g. the original game's data). The remaster may have rebalanced values,
/// so matching is by score over "stable" fields rather than exact bytes, and a
/// few record sizes around the reference one are tried in case records grew.
/// </summary>
public sealed class TableLocator
{
    public required TableSchema Schema { get; init; }

    /// <summary>The reference table: Schema.RecordCount records of Schema.RecordSize bytes.</summary>
    public required byte[] Reference { get; init; }

    /// <summary>Fields compared when scoring; pick ones a remaster is unlikely to change.</summary>
    public required IReadOnlyList<string> StableFields { get; init; }

    /// <summary>Records whose Move/Jump-style bytes are checked first, to skip most positions quickly.</summary>
    public required IReadOnlyList<int> AnchorRecords { get; init; }

    /// <summary>Byte offsets (within a record) checked on the anchor records.</summary>
    public required IReadOnlyList<int> AnchorOffsets { get; init; }

    public IReadOnlyList<int> RecordSizes { get; init; } = [];

    // Below ~0.7 are mostly the real table seen one record off (neighbouring
    // jobs look alike); a remaster's rebalancing still leaves the true spot well above.
    public double MinimumScore { get; init; } = 0.8;

    public IEnumerable<LocatorMatch> Find(byte[] memory)
    {
        var sizes = RecordSizes.Count > 0 ? RecordSizes : [Schema.RecordSize];
        var stableOffsets = StableFields
            .Select(Schema.Field)
            .SelectMany(f => Enumerable.Range(f.Offset, f.Size))
            .ToArray();
        var matches = new List<LocatorMatch>();

        foreach (var size in sizes)
        {
            if (size < Schema.RecordSize - 4)
                continue;
            // The last record is read with the reference record's length.
            long last = memory.LongLength - ((long)(Schema.RecordCount - 1) * size + Schema.RecordSize);
            for (long start = 0; start <= last; start++)
            {
                if (!AnyAnchorMatches(memory, start, size))
                    continue;
                var score = Score(memory, start, size, stableOffsets);
                if (score >= MinimumScore)
                    matches.Add(new LocatorMatch(start, size, score));
            }
        }

        return matches.OrderByDescending(m => m.Score);
    }

    // True when at least one anchor record has all its anchor bytes as in the reference.
    private bool AnyAnchorMatches(byte[] memory, long start, int size)
    {
        foreach (var record in AnchorRecords)
        {
            var all = true;
            foreach (var offset in AnchorOffsets)
            {
                if (memory[start + (long)record * size + offset] != Reference[record * Schema.RecordSize + offset])
                {
                    all = false;
                    break;
                }
            }
            if (all)
                return true;
        }
        return false;
    }

    private double Score(byte[] memory, long start, int size, int[] stableOffsets)
    {
        var same = 0;
        var total = 0;
        for (var record = 0; record < Schema.RecordCount; record++)
        {
            foreach (var offset in stableOffsets)
            {
                total++;
                if (memory[start + (long)record * size + offset] == Reference[record * Schema.RecordSize + offset])
                    same++;
            }
        }
        return total == 0 ? 0 : (double)same / total;
    }

    /// <summary>The usual settings for the job table.</summary>
    public static TableLocator ForJobs(byte[] reference) => new()
    {
        Schema = Tables.Schemas.Jobs.Schema,
        Reference = reference,
        StableFields = ["SkillSet", "Move", "Jump", "CEvade", "AbsorbElement", "CancelElement", "HalfElement", "WeakElement", "MPortrait", "MPalette", "MGraphic"],
        // Squire, Chemist, Knight, Archer, Monk: Move and Jump.
        AnchorRecords = [0x4A, 0x4B, 0x4C, 0x4D, 0x4E],
        AnchorOffsets = [24, 25],
        RecordSizes = [48, 49, 50, 52, 56, 64],
    };
}
