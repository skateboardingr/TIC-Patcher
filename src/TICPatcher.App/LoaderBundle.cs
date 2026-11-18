using System.Reflection;
using System.Text.Json.Nodes;
using TICPatcher.Core.Mods;

namespace TICPatcher.App;

/// <summary>The copy of TIC Loader built into the editor, installed into Reloaded-II when missing or older.</summary>
internal static class LoaderBundle
{
    private const string Prefix = "TICLoader/";

    private static IEnumerable<string> Resources =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal));

    public static IEnumerable<(string Name, byte[] Contents)> Files() =>
        Resources.Select(name => (name[Prefix.Length..], Read(name)));

    public static Version? Version =>
        Resources.FirstOrDefault(n => n == Prefix + "ModConfig.json") is { } config
            && Version.TryParse((string?)JsonNode.Parse(Read(config))?["ModVersion"], out var version) ? version : null;

    /// <summary>Installs or updates TIC Loader; returns a message if it did anything.</summary>
    public static string? EnsureInstalled(ReloadedSetup reloaded)
    {
        if (Version is not { } bundled)
            return null;
        var installed = reloaded.LoaderVersion();
        if (installed is not null && installed >= bundled)
            return null;
        reloaded.InstallLoader(Files());
        return installed is null ? $"Installed TIC Loader {bundled}." : $"Updated TIC Loader {installed} → {bundled}.";
    }

    private static byte[] Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
