using System.Globalization;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using TICLoader.Interfaces;

namespace TICFormatSample;

/// <summary>
/// The whole bridge: get TIC Loader's API and register a format. TIC Loader
/// does the rest (load order, merging, checking the game version, writing
/// the game's memory, logging). ModConfig.json must depend on "tic.loader".
/// </summary>
public class Startup : IMod
{
    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
    {
        var modLoader = (IModLoader)loaderApi;
        var logger = (ILogger)modLoader.GetLogger();
        if (modLoader.GetController<ITICLoader>() is not { } controller || !controller.TryGetTarget(out var tic))
        {
            logger.WriteLine("[CSV format] TIC Loader isn't loaded; enable it first.");
            return;
        }
        tic.AddModFormat(new CsvFormat());
    }

    public void Suspend() { }
    public void Resume() { }
    public void Unload() { }
    public bool CanUnload() => false;
    public bool CanSuspend() => false;
    public Action Disposing => () => { };
}

/// <summary>
/// Reads TIC/tables.csv: "Table,Id,Field,Value" per line. A real bridge reads
/// another loader's files here and maps its table and field names to TIC
/// Loader's (ITICLoader.Tables lists them all, with byte offsets).
/// </summary>
public sealed class CsvFormat : ITICModFormat
{
    public string Name => "CSV tables";

    public IReadOnlyList<TICFieldEdit>? Read(string modId, string modFolder)
    {
        var path = Path.Combine(modFolder, "TIC", "tables.csv");
        if (!File.Exists(path))
            return null; // not a mod in this format: TIC Loader moves on

        var edits = new List<TICFieldEdit>();
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#'))
                continue;
            var parts = text.Split(',', 4, StringSplitOptions.TrimEntries);
            if (parts.Length != 4)
                throw new FormatException($"tables.csv line {number}: expected Table,Id,Field,Value");
            edits.Add(new TICFieldEdit(parts[0], ParseId(parts[1], number), parts[2], parts[3]));
        }
        return edits; // TIC Loader validates every edit and reports mistakes clearly
    }

    private static int ParseId(string text, int line) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex
        : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number
        : throw new FormatException($"tables.csv line {line}: \"{text}\" isn't a record id");
}
