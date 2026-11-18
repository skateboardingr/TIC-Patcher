using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Tests;

// Generated data only.
public class ProjectTests
{
    private static VanillaData Vanilla()
    {
        var random = new Random(20);
        return new VanillaData
        {
            BuildName = "test",
            TimeDateStamp = 1,
            SizeOfImage = 2,
            Tables = AllTables.List.ToDictionary(t => t.Name, t =>
            {
                var bytes = new byte[t.Length];
                random.NextBytes(bytes);
                return bytes;
            }),
        };
    }

    [Fact]
    public void ChangesBackToTheOriginalAreDropped()
    {
        var project = new ModProject(Vanilla());
        var schema = Items.Common;
        var original = project.Original(schema, 0xF0, "Price");

        project.Set(schema, 0xF0, "Price", "10");
        Assert.Equal("10", project.Current(schema, 0xF0, "Price"));
        Assert.True(project.IsChanged(schema, 0xF0));

        project.Set(schema, 0xF0, "Price", original);
        Assert.False(project.IsChanged(schema, 0xF0));
        Assert.True(project.Edits.IsEmpty);
    }

    [Fact]
    public void FlagsAreStoredInBitOrder()
    {
        var project = new ModProject(Vanilla());
        project.Set(Jobs.Schema, 0x4A, "Equipment", "Sword, Knife");
        Assert.Equal("Knife, Sword", project.Edits.Get("Jobs", 0x4A, "Equipment"));
        Assert.Throws<FormatException>(() => project.Set(Jobs.Schema, 0x4A, "Equipment", "Spoon"));
    }

    [Fact]
    public void JsonRoundTrips()
    {
        var edits = new TableEdits();
        edits.Set("Items", 0xF0, "Price", "10");
        edits.Set("Jobs", 0x01, "Move", "6");
        edits.Set("Jobs", 0x4A, "Equipment", "Knife, Sword");
        var json = edits.ToJson();

        Assert.True(json.IndexOf("\"Jobs\"") < json.IndexOf("\"Items\""), "tables in schema order");
        Assert.Contains("\"0xF0\"", json);
        Assert.Contains("\"Move\": 6", json);
        Assert.Contains("\"Equipment\": \"Knife, Sword\"", json);
        Assert.Equal(edits.All.ToHashSet(), TableEdits.Parse(json).All.ToHashSet());
    }

    [Fact]
    public void VanillaSavesAndLoads()
    {
        var vanilla = Vanilla();
        var path = Path.Combine(Path.GetTempPath(), $"tic-vanilla-{Guid.NewGuid():N}.json");
        try
        {
            vanilla.Save(path);
            var loaded = VanillaData.Load(path);
            Assert.Equal(vanilla.Tables["Jobs"], loaded.Tables["Jobs"]);
            Assert.Equal(vanilla.TimeDateStamp, loaded.TimeDateStamp);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NamesComeFromFFTPatchersLists()
    {
        Assert.Equal("Squire", Names.Get(Names.Jobs, 0x01));
        Assert.Equal("Cure", Names.Get(Names.Abilities, 1));
        Assert.Equal("Potion", Names.Get(Names.Items, 0xF0));
        Assert.Null(Names.Get(Names.Items, 9999));
        Assert.Equal("Ring of Aptitude", Names.Get(Names.Items, 0x104)); // not WotL's Valhalla
        Assert.Null(Names.Get(Names.Items, 0x138)); // Tynar Rouge isn't in TIC
    }
}

public class SessionTests
{
    [Fact]
    public void SessionsRoundTripAndBadFilesAreIgnored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tic-session-{Guid.NewGuid():N}.json");
        try
        {
            new EditorSession("tic.mod.ng", "NG+ Mod", "{ \"Jobs\": { \"0x01\": { \"Move\": 6 } } }").Save(path);
            var loaded = EditorSession.Load(path)!;
            Assert.Equal("tic.mod.ng", loaded.ModId);
            Assert.Equal("NG+ Mod", loaded.ModName);
            Assert.Equal("6", TableEdits.Parse(loaded.TablesJson).Get("Jobs", 1, "Move"));

            File.WriteAllText(path, "{ broken");
            Assert.Null(EditorSession.Load(path));
            Assert.Null(EditorSession.Load(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
