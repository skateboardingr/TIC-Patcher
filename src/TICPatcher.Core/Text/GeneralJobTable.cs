namespace TICPatcher.Core.Text;

/// <summary>
/// The job tree's table, generaljob.nxd (0004.pac): one row per generic job in the tree, row N
/// being generic job N (job id 0x4A + N; 20 and 21 would be A0 and A1), plus a last, shorter row
/// whose number (20 in the game) the links use for "none". Read and written byte for byte as the
/// game ships it, so rows can be added (TIC Loader's file replacement hands the game a bigger file).
/// <para>
/// File: the NXDF header (0x30 bytes, index at 0x20, row count at 0x24); an index of (id, offset
/// from the index entry) pairs; the rows one after another; a pool of 16-bit prerequisite levels,
/// rows in order; one empty string. A row: 0x7C bytes of fields, then its JP table, its
/// prerequisite jobs and its third list, inline. Its fields are relative to the row's start:
/// +0x00 0, +0x04 the empty string, then (offset, count) of the JP table (+0x08), prerequisite
/// jobs (+0x10), prerequisite levels (+0x18, in the pool) and the third list (+0x20); +0x28 16
/// links to other rows; +0x68 5 flags.
/// </para>
/// </summary>
public sealed class GeneralJobTable
{
    private const int HeaderSize = 0x30;
    private const int FieldsSize = 0x7C;

    public sealed class Row
    {
        public int Id { get; set; }
        public uint Key { get; set; }
        /// <summary>JP needed for each job level (9 values, the first 0), empty in the "none" row.</summary>
        public List<uint> JpPerLevel { get; set; } = [];
        /// <summary>The prerequisite jobs (generic job numbers) and the level needed in each.</summary>
        public List<(uint Job, ushort Level)> Prerequisites { get; set; } = [];
        /// <summary>A number per prerequisite whose meaning isn't known yet (Mime's: 2, 1, 3, 3, 2, 3).</summary>
        public List<uint> PrerequisiteExtra { get; set; } = [];
        /// <summary>16 other rows (the "none" row's number where there's nothing).</summary>
        public uint[] Links { get; set; } = new uint[16];
        public uint[] Flags { get; set; } = new uint[5];
    }

    private readonly byte[] _header;

    public GeneralJobTable(byte[] header, List<Row> rows)
    {
        _header = header;
        Rows = rows;
    }

    public List<Row> Rows { get; }

    public static GeneralJobTable Parse(byte[] file)
    {
        var header = file.AsSpan(0, HeaderSize).ToArray();
        var index = BitConverter.ToInt32(file, 0x20);
        var count = BitConverter.ToInt32(file, 0x24);
        var rows = new List<Row>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = index + i * 8;
            var r = entry + BitConverter.ToInt32(file, entry + 4);
            uint U(int at) => BitConverter.ToUInt32(file, r + at);
            List<uint> Words(int offsetField) =>
                [.. Enumerable.Range(0, (int)U(offsetField + 4)).Select(k => BitConverter.ToUInt32(file, r + (int)U(offsetField) + 4 * k))];
            var jobs = Words(0x10);
            var levels = Enumerable.Range(0, (int)U(0x1C)).Select(k => BitConverter.ToUInt16(file, r + (int)U(0x18) + 2 * k)).ToList();
            if (levels.Count != jobs.Count)
                throw new FormatException($"generaljob.nxd row {i}: {jobs.Count} prerequisite jobs but {levels.Count} levels");
            rows.Add(new Row
            {
                Id = BitConverter.ToInt32(file, entry),
                Key = U(0),
                JpPerLevel = Words(0x08),
                Prerequisites = [.. jobs.Zip(levels)],
                PrerequisiteExtra = Words(0x20),
                Links = [.. Enumerable.Range(0, 16).Select(k => U(0x28 + 4 * k))],
                Flags = [.. Enumerable.Range(0, 5).Select(k => U(0x68 + 4 * k))],
            });
        }
        return new GeneralJobTable(header, rows);
    }

    public byte[] Build()
    {
        var count = Rows.Count;
        var starts = new int[count];
        var at = HeaderSize + count * 8;
        for (var i = 0; i < count; i++)
        {
            starts[i] = at;
            at += FieldsSize + 4 * (Rows[i].JpPerLevel.Count + Rows[i].Prerequisites.Count + Rows[i].PrerequisiteExtra.Count);
        }
        var poolAt = new int[count];
        for (var i = 0; i < count; i++)
        {
            poolAt[i] = at;
            at += 2 * Rows[i].Prerequisites.Count;
        }
        var emptyString = at;
        var file = new byte[emptyString + 1];
        _header.CopyTo(file, 0);
        BitConverter.TryWriteBytes(file.AsSpan(0x20), HeaderSize);
        BitConverter.TryWriteBytes(file.AsSpan(0x24), count);
        for (var i = 0; i < count; i++)
        {
            var row = Rows[i];
            var entry = HeaderSize + i * 8;
            var r = starts[i];
            void Put(int offset, uint value) => BitConverter.TryWriteBytes(file.AsSpan(r + offset), value);
            BitConverter.TryWriteBytes(file.AsSpan(entry), row.Id);
            BitConverter.TryWriteBytes(file.AsSpan(entry + 4), r - entry);
            var jpAt = FieldsSize;
            var jobsAt = jpAt + 4 * row.JpPerLevel.Count;
            var extraAt = jobsAt + 4 * row.Prerequisites.Count;
            Put(0x00, row.Key);
            Put(0x04, (uint)(emptyString - r));
            Put(0x08, (uint)jpAt);
            Put(0x0C, (uint)row.JpPerLevel.Count);
            Put(0x10, (uint)jobsAt);
            Put(0x14, (uint)row.Prerequisites.Count);
            Put(0x18, (uint)(poolAt[i] - r));
            Put(0x1C, (uint)row.Prerequisites.Count);
            Put(0x20, (uint)extraAt);
            Put(0x24, (uint)row.PrerequisiteExtra.Count);
            for (var k = 0; k < 16; k++)
                Put(0x28 + 4 * k, row.Links[k]);
            for (var k = 0; k < 5; k++)
                Put(0x68 + 4 * k, row.Flags[k]);
            for (var k = 0; k < row.JpPerLevel.Count; k++)
                Put(jpAt + 4 * k, row.JpPerLevel[k]);
            for (var k = 0; k < row.Prerequisites.Count; k++)
            {
                Put(jobsAt + 4 * k, row.Prerequisites[k].Job);
                BitConverter.TryWriteBytes(file.AsSpan(poolAt[i] + 2 * k), row.Prerequisites[k].Level);
            }
            for (var k = 0; k < row.PrerequisiteExtra.Count; k++)
                Put(extraAt + 4 * k, row.PrerequisiteExtra[k]);
        }
        return file;
    }
}
