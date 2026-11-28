using TICPatcher.Core.Text;

namespace TICPatcher.Core.Tests;

// Generated data only: a made-up job tree table shaped like the game's.
public class GeneralJobTableTests
{
    private static GeneralJobTable Sample()
    {
        var header = new byte[0x30];
        "NXDF"u8.CopyTo(header);
        var rows = new List<GeneralJobTable.Row>();
        for (var i = 0; i < 4; i++)
            rows.Add(new GeneralJobTable.Row
            {
                Id = i,
                JpPerLevel = [0, 100, 200, 400, 700, 1100, 1600, 2200, 3000],
                Prerequisites = [.. Enumerable.Range(0, i).Select(k => ((uint)k, (ushort)(k + 2)))],
                PrerequisiteExtra = [.. Enumerable.Range(0, i).Select(k => (uint)(k % 3))],
                Links = [.. Enumerable.Range(0, 16).Select(k => (uint)((i + k) % 5))],
                Flags = [1, 0, 0, 0, (uint)(i > 0 ? 1 : 0)],
            });
        rows.Add(new GeneralJobTable.Row { Id = 4, Links = [.. Enumerable.Repeat(4u, 16)] }); // "none"
        return new GeneralJobTable(header, rows);
    }

    [Fact]
    public void BuildsAndReadsBackTheSameRows()
    {
        var file = Sample().Build();
        var again = GeneralJobTable.Parse(file);
        Assert.Equal(file, again.Build());
        Assert.Equal(5, again.Rows.Count);
        Assert.Equal([(0u, (ushort)2), (1u, (ushort)3), (2u, (ushort)4)], again.Rows[3].Prerequisites);
        Assert.Empty(again.Rows[4].JpPerLevel);
    }

    [Fact]
    public void AddsARowBeforeTheNoneRow()
    {
        var table = GeneralJobTable.Parse(Sample().Build());
        table.Rows.Insert(4, new GeneralJobTable.Row
        {
            Id = 4,
            JpPerLevel = [0, 100, 200, 400, 700, 1100, 1600, 2200, 3000],
            Prerequisites = [(0, 1)],
            PrerequisiteExtra = [0],
            Links = [.. Enumerable.Repeat(5u, 16)],
            Flags = [0, 0, 0, 0, 1],
        });
        table.Rows[5].Id = 5;
        var grown = GeneralJobTable.Parse(table.Build());
        Assert.Equal(6, grown.Rows.Count);
        Assert.Equal([(0u, (ushort)1)], grown.Rows[4].Prerequisites);
        Assert.Equal(5, grown.Rows[5].Id);
    }
}
