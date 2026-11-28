using TICPatcher.Core.Tables.Schemas;
using TICPatcher.Core.Text;

namespace TICPatcher.Core.Mods;

/// <summary>
/// The editor's view of the job tree's prerequisites (<see cref="Jobs.JobTree"/>: per generic job,
/// up to 8 (job, level) pairs) and the two game files it drives: generaljob.nxd, which the job
/// tree screen shows (given to the game by TIC Loader's file replacement), and the executable's
/// <see cref="SkillSets.JobRequirements"/>, which decides unlocking and holds each job's
/// requirements with their own requirements folded in (so the game shipped them).
/// </summary>
public static class JobTreeFile
{
    /// <summary>Where the mod keeps the file: TIC/files/&lt;archive&gt;/&lt;name&gt;.</summary>
    public const string ModPath = "TIC/files/0004/generaljob.nxd";
    public const string VanillaKey = "0004/generaljob.nxd";
    public const int Slots = 8;
    public const byte None = 0xFF;

    /// <summary>The table's bytes for the game's file: each job's prerequisites, unused slots None.</summary>
    public static byte[] Encode(GeneralJobTable table)
    {
        var bytes = new byte[Jobs.JobTree.Length];
        for (var job = 0; job < Jobs.JobTree.RecordCount; job++)
        {
            var record = bytes.AsSpan(job * Jobs.JobTree.RecordSize, Jobs.JobTree.RecordSize);
            record.Fill(None);
            for (var k = 0; k < Slots; k++)
                record[2 * k + 1] = 0;
            if (job >= table.Rows.Count)
                continue;
            var prerequisites = table.Rows[job].Prerequisites;
            for (var k = 0; k < Math.Min(Slots, prerequisites.Count); k++)
            {
                record[2 * k] = (byte)prerequisites[k].Job;
                record[2 * k + 1] = (byte)prerequisites[k].Level;
            }
        }
        return bytes;
    }

    /// <summary>Each job's direct prerequisites from the table's bytes: (generic job, level).</summary>
    public static List<(int Job, int Level)>[] Prerequisites(ReadOnlySpan<byte> tableBytes)
    {
        var result = new List<(int, int)>[Jobs.JobTree.RecordCount];
        for (var job = 0; job < result.Length; job++)
        {
            result[job] = [];
            var record = tableBytes.Slice(job * Jobs.JobTree.RecordSize, Jobs.JobTree.RecordSize);
            for (var k = 0; k < Slots; k++)
                if (record[2 * k] != None && record[2 * k + 1] > 0)
                    result[job].Add((record[2 * k], record[2 * k + 1]));
        }
        return result;
    }

    /// <summary>
    /// The game's file with the table's prerequisites: a prerequisite already there keeps its
    /// third value (meaning unknown), a new one gets 0; the has-prerequisites flag follows.
    /// </summary>
    public static byte[] Build(byte[] originalFile, ReadOnlySpan<byte> tableBytes)
    {
        var table = GeneralJobTable.Parse(originalFile);
        var wanted = Prerequisites(tableBytes);
        for (var job = 0; job < Math.Min(wanted.Length, table.Rows.Count); job++)
        {
            var row = table.Rows[job];
            var extra = row.Prerequisites.Select(p => (int)p.Job).Zip(row.PrerequisiteExtra).ToDictionary(p => p.First, p => p.Second);
            row.Prerequisites = [.. wanted[job].Select(p => ((uint)p.Job, (ushort)p.Level))];
            row.PrerequisiteExtra = [.. wanted[job].Select(p => extra.GetValueOrDefault(p.Job))];
            row.Flags[3] = row.Prerequisites.Count > 0 ? 1u : 0u;
        }
        return table.Build();
    }

    /// <summary>
    /// The level each job needs in every other, its prerequisites' requirements included (the
    /// executable's JobRequirements): [job][other job]. Cycles are cut where they close.
    /// </summary>
    public static int[][] Cumulative(List<(int Job, int Level)>[] direct)
    {
        var count = Math.Max(direct.Length, 24);
        var result = new int[direct.Length][];
        int[] Of(int job, HashSet<int> visiting)
        {
            var levels = new int[count];
            if (job < 0 || job >= direct.Length || !visiting.Add(job))
                return levels;
            foreach (var (other, level) in direct[job])
            {
                if (other < count)
                    levels[other] = Math.Max(levels[other], level);
                var deeper = Of(other, visiting);
                for (var k = 0; k < count; k++)
                    levels[k] = Math.Max(levels[k], deeper[k]);
            }
            visiting.Remove(job);
            return levels;
        }
        for (var job = 0; job < direct.Length; job++)
            result[job] = Of(job, []);
        return result;
    }
}
