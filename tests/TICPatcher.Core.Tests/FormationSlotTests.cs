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
        // Story sprite n uses slot n - 1 and that slot shows it, except sprite 0x26, a guest on slot 0x89.
        for (var sprite = 1; sprite <= 0x18; sprite++)
        {
            tables[Jobs.SpriteFormationSlots.Name][sprite * 2] = (byte)(sprite - 1);
            tables[Jobs.FormationSlotSprites.Name][(sprite - 1) * 4] = (byte)sprite;
        }
        tables[Jobs.SpriteFormationSlots.Name][0x26 * 2] = 0x89;
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
        // Adding again keeps the same slot; another guest gets the next spare one.
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
        var guests = Enumerable.Range(0x30, FormationSlots.Spare.Count + 1).ToList();
        foreach (var guest in guests.SkipLast(1))
            Assert.NotNull(FormationSlots.AddToParty(project, guest));
        Assert.Null(FormationSlots.AddToParty(project, guests[^1]));
    }
}
