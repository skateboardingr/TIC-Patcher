using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TICPatcher.Core.Mods;

/// <summary>
/// Finds the installed game's executable: from Reloaded-II's settings if the
/// game was added there, else from Steam's library folders (Windows, Linux and
/// Steam Deck).
/// </summary>
public static partial class GameLocator
{
    public const string ExeName = "FFT_enhanced.exe";
    public const string SteamFolder = "FINAL FANTASY TACTICS - The Ivalice Chronicles";

    public static string? Find()
    {
        if (ReloadedSetup.Find(out _) is { } reloaded && reloaded.GameAppConfig() is { } config
            && (string?)JsonNode.Parse(File.ReadAllText(config))?["AppLocation"] is { } location && File.Exists(location))
            return location;
        return SteamLibraries().Select(library => Path.Combine(library, "steamapps", "common", SteamFolder, ExeName)).FirstOrDefault(File.Exists);
    }

    /// <summary>Steam library folders listed in libraryfolders.vdf.</summary>
    public static IEnumerable<string> SteamLibraries()
    {
        foreach (var steam in SteamRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            yield return steam;
            if (!File.Exists(vdf))
                continue;
            foreach (Match match in LibraryPath().Matches(File.ReadAllText(vdf)))
                yield return match.Groups[1].Value.Replace(@"\\", @"\");
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string path)
                yield return path.Replace('/', '\\');
            yield return @"C:\Program Files (x86)\Steam";
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPath();
}
