using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Memory;

/// <summary>
/// Finds the job table without any reference copy of the game's data: the
/// generic jobs Squire (0x4A) to Mime (0x5D) use skill sets 5, 6, 7 ... 24,
/// one per job, at the start of consecutive 49-byte records. Measured in The
/// Ivalice Chronicles (build 24304444) and the same in War of the Lions; see
/// docs/findings.md. The pattern is specific enough to be unique in practice.
/// </summary>
public static class JobTableFingerprint
{
    private const byte FirstSkillSet = 5;

    /// <summary>Offsets of every place in <paramref name="memory"/> where the job table could start.</summary>
    public static List<long> Find(ReadOnlySpan<byte> memory)
    {
        var size = Jobs.Schema.RecordSize;
        var count = Jobs.LastGenericJob - Jobs.FirstGenericJob + 1;
        var firstGeneric = (long)Jobs.FirstGenericJob * size;
        var tableLength = (long)Jobs.Schema.RecordCount * size;
        var found = new List<long>();

        // Squire's skill set byte is at table start + 0x4A records.
        for (long squire = firstGeneric; squire + (count - 1) * size < memory.Length; squire++)
        {
            if (memory[(int)squire] != FirstSkillSet)
                continue;
            var start = squire - firstGeneric;
            if (start + tableLength > memory.Length)
                break;
            var all = true;
            for (var i = 1; i < count && all; i++)
                all = memory[(int)(squire + (long)i * size)] == FirstSkillSet + i;
            if (all)
                found.Add(start);
        }
        return found;
    }
}
