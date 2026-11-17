using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TICPatcher.Core.Mods;

/// <summary>
/// The player's Reloaded-II, found through its own settings
/// (%APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json): installs a TIC mod
/// into its Mods folder, turns it on for the game, and launches the game
/// through it. Settings files are edited in place, keeping everything else.
/// </summary>
public sealed class ReloadedSetup
{
    public const string GameAppId = "fft_enhanced.exe";
    public const string LoaderModId = "tic.loader";

    public required string LauncherPath { get; init; }
    public required string ModsFolder { get; init; }
    public required string AppsFolder { get; init; }

    public static string DefaultSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Reloaded-Mod-Loader-II", "ReloadedII.json");

    /// <summary>Reloaded-II as configured on this PC, or null with the reason.</summary>
    public static ReloadedSetup? Find(out string problem, string? settingsPath = null)
    {
        settingsPath ??= DefaultSettingsPath;
        problem = "";
        if (!File.Exists(settingsPath))
        {
            problem = "Reloaded-II isn't set up on this PC (install it and run it once).";
            return null;
        }
        var settings = JsonNode.Parse(File.ReadAllText(settingsPath));
        var launcher = (string?)settings?["LauncherPath"];
        var mods = (string?)settings?["ModConfigDirectory"];
        var apps = (string?)settings?["ApplicationConfigDirectory"];
        if (launcher is null || mods is null || apps is null)
        {
            problem = "Reloaded-II's settings are incomplete; run Reloaded-II once.";
            return null;
        }
        return new ReloadedSetup { LauncherPath = launcher, ModsFolder = mods, AppsFolder = apps };
    }

    /// <summary>The game's app settings file, or null if the game hasn't been added to Reloaded-II.</summary>
    public string? GameAppConfig() =>
        Directory.Exists(AppsFolder)
            ? Directory.EnumerateFiles(AppsFolder, "AppConfig.json", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals((string?)JsonNode.Parse(File.ReadAllText(path))?["AppId"], GameAppId, StringComparison.OrdinalIgnoreCase))
            : null;

    public bool LoaderInstalled => File.Exists(Path.Combine(ModsFolder, LoaderModId, "ModConfig.json"));

    /// <summary>A mod id for a mod name, e.g. "Faster Chocobos" becomes "tic.mod.faster-chocobos".</summary>
    public static string IdFor(string name)
    {
        var slug = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
            slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        var text = string.Join('-', slug.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return "tic.mod." + (text.Length > 0 ? text : "untitled");
    }

    /// <summary>
    /// Writes the mod into Reloaded-II's Mods folder: ModConfig.json (kept and
    /// updated if it exists) and TIC/tables.json. Returns the mod's folder.
    /// </summary>
    public string Install(string modId, string name, string author, string description, string tablesJson)
    {
        var folder = Path.Combine(ModsFolder, modId);
        Directory.CreateDirectory(Path.Combine(folder, "TIC"));
        var configPath = Path.Combine(folder, "ModConfig.json");
        var config = File.Exists(configPath) ? JsonNode.Parse(File.ReadAllText(configPath))!.AsObject() : NewModConfig(modId);
        config["ModName"] = name;
        config["ModAuthor"] = author;
        config["ModDescription"] = description;
        if (config["ModDependencies"] is not JsonArray dependencies || !dependencies.Any(d => (string?)d == LoaderModId))
            config["ModDependencies"] = new JsonArray(LoaderModId);
        WriteAtomically(configPath, config.ToJsonString(Indented));
        WriteAtomically(Path.Combine(folder, ModCollector.TablesFile), tablesJson);
        return folder;
    }

    /// <summary>Turns the mod on for the game in Reloaded-II. Returns false if the game isn't in Reloaded-II.</summary>
    public bool Enable(string modId)
    {
        if (GameAppConfig() is not { } path)
            return false;
        var app = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var enabled = app["EnabledMods"] as JsonArray ?? [];
        if (!enabled.Any(m => string.Equals((string?)m, modId, StringComparison.OrdinalIgnoreCase)))
            enabled.Add(modId);
        app["EnabledMods"] = enabled;
        WriteAtomically(path, app.ToJsonString(Indented));
        return true;
    }

    /// <summary>The command that launches the game through Reloaded-II.</summary>
    public ProcessStartInfo LaunchCommand()
    {
        var app = GameAppConfig() is { } path ? JsonNode.Parse(File.ReadAllText(path)) : null;
        var game = (string?)app?["AppLocation"] ?? throw new InvalidOperationException("Add the game to Reloaded-II first.");
        var start = new ProcessStartInfo(LauncherPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(LauncherPath) };
        start.ArgumentList.Add("--launch");
        start.ArgumentList.Add(game);
        return start;
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static JsonObject NewModConfig(string modId) => new()
    {
        ["ModId"] = modId,
        ["ModName"] = modId,
        ["ModAuthor"] = "",
        ["ModVersion"] = "1.0.0",
        ["ModDescription"] = "",
        ["ModDll"] = "",
        ["ModIcon"] = "",
        ["ModR2RManagedDll32"] = "",
        ["ModR2RManagedDll64"] = "",
        ["ModNativeDll32"] = "",
        ["ModNativeDll64"] = "",
        ["IsLibrary"] = false,
        ["ReleaseMetadataFileName"] = "",
        ["PluginData"] = new JsonObject(),
        ["IsUniversalMod"] = false,
        ["ModDependencies"] = new JsonArray(LoaderModId),
        ["OptionalDependencies"] = new JsonArray(),
        ["SupportedAppId"] = new JsonArray(GameAppId),
        ["ProjectUrl"] = "",
    };

    private static void WriteAtomically(string path, string text)
    {
        File.WriteAllText(path + ".tmp", text);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
