using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables.Schemas;
using TICPatcher.Core.Text;

namespace TICPatcher.Core.Tests;

// Generated data only: a made-up job tree shaped like the game's (20 jobs and a "none" row).
public class JobTreeTests
{
    private static byte[] TreeFile()
    {
        var header = new byte[0x30];
        "NXDF"u8.CopyTo(header);
        var rows = new List<GeneralJobTable.Row>();
        for (var job = 0; job < 20; job++)
            rows.Add(new GeneralJobTable.Row
            {
                Id = job,
                JpPerLevel = [0, 100, 200, 400, 700, 1100, 1600, 2200, 3000],
                // A chain: each job needs the one before it at level 2 (Squire needs nothing).
                Prerequisites = job == 0 ? [] : [((uint)(job - 1), (ushort)2)],
                PrerequisiteExtra = job == 0 ? [] : [7],
                Links = [.. Enumerable.Repeat(20u, 16)],
                Flags = [0, 0, 0, job == 0 ? 0u : 1u, 0],
            });
        rows.Add(new GeneralJobTable.Row { Id = 20, Links = [.. Enumerable.Repeat(20u, 16)] });
        return new GeneralJobTable(header, rows).Build();
    }

    private static ModProject Project()
    {
        var file = TreeFile();
        var tables = AllTables.List.ToDictionary(t => t.Name, t => new byte[t.Length]);
        tables[Jobs.JobTree.Name] = JobTreeFile.Encode(GeneralJobTable.Parse(file));
        var levels = JobTreeFile.Cumulative(JobTreeFile.Prerequisites(tables[Jobs.JobTree.Name]));
        // JobRequirements as the game ships it: the chain's cumulative levels (record N = job N + 1).
        var requirements = tables[SkillSets.JobRequirements.Name];
        for (var job = 1; job < 20; job++)
            for (var other = 0; other < 24; other++)
                if (levels[job][other] > 0)
                    requirements[(job - 1) * 12 + other / 2] |= (byte)(other % 2 == 0 ? levels[job][other] << 4 : levels[job][other]);
        var vanilla = new VanillaData { BuildName = "test", TimeDateStamp = 1, SizeOfImage = 2, Tables = tables };
        vanilla.Files[JobTreeFile.VanillaKey] = file;
        return new ModProject(vanilla);
    }

    [Fact]
    public void CumulativeRequirementsFollowTheChain()
    {
        var levels = JobTreeFile.Cumulative(JobTreeFile.Prerequisites(JobTreeFile.Encode(GeneralJobTable.Parse(TreeFile()))));
        Assert.Equal(2, levels[3][0]); // job 3 needs job 0 through 1 and 2
        Assert.Equal(2, levels[3][2]);
        Assert.Equal(0, levels[3][4]);
    }

    [Fact]
    public void EditingTheTreeUpdatesRequirementsAndTheFile()
    {
        var project = Project();
        Assert.Empty(project.ModFiles());
        // Job 5 (record 4 in JobRequirements) needs job 4 at 2; make it job 1 at 6 instead.
        project.Set(Jobs.JobTree, 5, "Job1", "1");
        project.Set(Jobs.JobTree, 5, "Level1", "6");
        Assert.Equal("6", project.Current(SkillSets.JobRequirements, 4, "Chemist"));
        Assert.Equal("2", project.Current(SkillSets.JobRequirements, 4, "Squire"));
        Assert.Equal("0", project.Current(SkillSets.JobRequirements, 4, "Monk"));
        // Job 6 needs job 5, so it now needs job 1 at 6 too.
        Assert.Equal("6", project.Current(SkillSets.JobRequirements, 5, "Chemist"));

        var file = Assert.Single(project.ModFiles());
        Assert.Equal(JobTreeFile.ModPath, file.Key);
        var row = GeneralJobTable.Parse(file.Value).Rows[5];
        Assert.Equal([(1u, (ushort)6)], row.Prerequisites);
        Assert.Equal([0u], row.PrerequisiteExtra); // a new prerequisite's third value

        // Undoing it puts everything back.
        project.Revert(Jobs.JobTree, 5);
        Assert.Empty(project.ModFiles());
        Assert.DoesNotContain(SkillSets.JobRequirements.Name, project.Edits.ChangedTables);
    }
}
