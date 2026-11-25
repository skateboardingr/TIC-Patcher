using TICPatcher.Core.Mods;
using TICPatcher.Core.Sprites;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Tests;

// Generated data only: made-up slot tables shaped like the game's.
public class FormationSlotTests
{
    private static ModProject Project()
    {
        var tables = AllTables.List.ToDictionary(t => t.Name, t => new byte[t.Length]);
        // Story sprite n uses slot n - 1 and that slot shows it, except sprite 0x26, an enemy-only character on slot 0x89.
        for (var sprite = 1; sprite <= 0x18; sprite++)
        {
            tables[Jobs.SpriteFormationSlots.Name][sprite * 2] = (byte)(sprite - 1);
            tables[Jobs.FormationSlotSprites.Name][(sprite - 1) * 4] = (byte)sprite;
        }
        tables[Jobs.SpriteFormationSlots.Name][0x26 * 2] = 0x89;
        // The sprite list: 0x26 on slot 0x89 showing it; 0x28 on 0x12, which shows another sprite (0x21).
        tables[Jobs.FormationSprites2.Name][0x26 * 2] = 0x89;
        tables[Jobs.SlotSprites.Name][0x89 * 4] = 0x26;
        tables[Jobs.FormationSprites2.Name][0x28 * 2] = 0x12;
        tables[Jobs.SlotSprites.Name][0x12 * 4] = 0x21;
        tables[Jobs.SlotSprites.Name][0x80 * 4] = 0x28;
        return new ModProject(new VanillaData { BuildName = "test", TimeDateStamp = 1, SizeOfImage = 2, Tables = tables });
    }

    [Fact]
    public void LendsSpareSlotsBothWaysAndGivesThemBack()
    {
        var project = Project();
        Assert.False(FormationSlots.InParty(project, 0x26));
        Assert.Null(FormationSlots.FormationSlot(project, 0x26));

        Assert.Equal(0x08, FormationSlots.AddToParty(project, 0x26));
        Assert.True(FormationSlots.InParty(project, 0x26));
        Assert.Equal(0x26, FormationSlots.SpriteIn(project, 0x08));
        Assert.Equal(0x08, FormationSlots.FormationSlot(project, 0x26));
        // Adding again keeps the same slot; another recruit gets the next spare one.
        Assert.Equal(0x08, FormationSlots.AddToParty(project, 0x26));
        Assert.Equal(0x10, FormationSlots.AddToParty(project, 0x2B));

        FormationSlots.RemoveFromParty(project, 0x26);
        Assert.False(FormationSlots.InParty(project, 0x26));
        Assert.Equal(0x09, FormationSlots.SpriteIn(project, 0x08)); // back to the game's own
        Assert.Equal(0x08, FormationSlots.AddToParty(project, 0x27)); // free again
    }

    [Fact]
    public void SaysWhenNoSpareSlotIsLeft()
    {
        var project = Project();
        var recruits = Enumerable.Range(0x30, FormationSlots.Spare.Count + 1).ToList();
        foreach (var recruit in recruits.SkipLast(1))
            Assert.NotNull(FormationSlots.AddToParty(project, recruit));
        Assert.Null(FormationSlots.AddToParty(project, recruits[^1]));
    }

    [Fact]
    public void PointsTheFaceAtTheSpriteWhenItShowsAnother()
    {
        var project = Project();
        FormationSlots.AddToParty(project, 0x26);
        Assert.False(project.IsChanged(Jobs.FormationSprites2, 0x26)); // already right
        Assert.Null(FormationSlots.FaceSlot(project, 0x28));
        FormationSlots.AddToParty(project, 0x28);
        Assert.Equal(0x80, FormationSlots.FaceSlot(project, 0x28));
    }

    [Fact]
    public void ReadsTheBrieflySeparateSpriteListCopies()
    {
        var edits = TableEdits.Parse("""{ "SpriteSlots2": { "0x28": { "Slot": 128 } } }""");
        Assert.Equal("128", edits.Get(Jobs.FormationSprites2.Name, 0x28, "FormationSprite2"));
        Assert.Empty(edits.UnknownTables);
    }

    [Fact]
    public void TheSpriteListIsWrittenToEveryCopy()
    {
        var build = Memory.GameBuilds.Known[0];
        Assert.Equal([0x680070, 0x6807C0, 0x680C00, 0x681A90, 0x683A70], build.OffsetsOf(Jobs.FormationSprites2.Name).ToArray());
        Assert.Equal([0x6829F0], build.OffsetsOf(Jobs.SpriteFormationSlots.Name).ToArray());
    }
}
