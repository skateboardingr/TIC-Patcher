using System.Text.Json.Nodes;
using TICPatcher.Core.Mods;

namespace TICPatcher.Core.Tests;

// A fake Reloaded-II setup in a temp folder.
public sealed class ReloadedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tic-reloaded-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ReloadedSetup Setup(bool withGame = true)
    {
        var apps = Path.Combine(_root, "Apps");
        Directory.CreateDirectory(_root);
        if (withGame)
        {
            Directory.CreateDirectory(Path.Combine(apps, "fft_enhanced.exe"));
            File.WriteAllText(Path.Combine(apps, "fft_enhanced.exe", "AppConfig.json"), """
                { "AppId": "fft_enhanced.exe", "AppLocation": "C:\\Games\\FFT_enhanced.exe", "EnabledMods": ["other.mod"], "SortedMods": [], "AutoInject": false }
                """);
        }
        var settings = Path.Combine(_root, "ReloadedII.json");
        File.WriteAllText(settings, new JsonObject
        {
            ["LauncherPath"] = Path.Combine(_root, "Reloaded-II.exe"),
            ["ModConfigDirectory"] = Path.Combine(_root, "Mods"),
            ["ApplicationConfigDirectory"] = apps,
            ["ShowConsole"] = true,
        }.ToJsonString());
        return ReloadedSetup.Find(out _, settings)!;
    }

    [Fact]
    public void InstallsEnablesAndKeepsOtherSettings()
    {
        var reloaded = Setup();
        var folder = reloaded.Install("tic.mod.test", "Test", "me", "desc", "{ }\n");

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "ModConfig.json")))!;
        Assert.Equal("tic.mod.test", (string?)config["ModId"]);
        Assert.Equal("tic.loader", (string?)config["ModDependencies"]![0]);
        Assert.Equal("{ }\n", File.ReadAllText(Path.Combine(folder, "TIC", "tables.json")));

        Assert.True(reloaded.Enable("tic.mod.test"));
        Assert.True(reloaded.Enable("tic.mod.test")); // twice is harmless
        var app = JsonNode.Parse(File.ReadAllText(reloaded.GameAppConfig()!))!;
        Assert.Equal(["other.mod", "tic.mod.test"], app["EnabledMods"]!.AsArray().Select(m => (string?)m));
        Assert.False((bool)app["AutoInject"]!);

        var launch = reloaded.LaunchCommand();
        Assert.Equal(["--launch", "C:\\Games\\FFT_enhanced.exe"], launch.ArgumentList);
    }

    [Fact]
    public void ReinstallKeepsTheModsOwnConfig()
    {
        var reloaded = Setup();
        var folder = reloaded.Install("tic.mod.test", "Test", "me", "desc", "{ }");
        var path = Path.Combine(folder, "ModConfig.json");
        var config = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        config["ModVersion"] = "2.0.0";
        File.WriteAllText(path, config.ToJsonString());

        reloaded.Install("tic.mod.test", "Renamed", "me", "desc", "{ }");
        var after = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("2.0.0", (string?)after["ModVersion"]);
        Assert.Equal("Renamed", (string?)after["ModName"]);
    }

    [Fact]
    public void ReportsWhatIsMissing()
    {
        Assert.Null(ReloadedSetup.Find(out var problem, Path.Combine(_root, "nowhere.json")));
        Assert.Contains("isn't set up", problem);
        var reloaded = Setup(withGame: false);
        Assert.False(reloaded.Enable("tic.mod.test"));
        Assert.Throws<InvalidOperationException>(() => reloaded.LaunchCommand());
    }

    [Theory]
    [InlineData("Faster Chocobos", "tic.mod.faster-chocobos")]
    [InlineData("  Ramza's Move+2!! ", "tic.mod.ramza-s-move-2")]
    [InlineData("???", "tic.mod.untitled")]
    public void ModIdsComeFromNames(string name, string id) => Assert.Equal(id, ReloadedSetup.IdFor(name));
}
