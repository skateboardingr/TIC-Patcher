using System.Diagnostics;
using System.Runtime.Versioning;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Loader;

/// <summary>
/// TIC Loader: once every mod has loaded, merges each enabled mod's
/// TIC/tables.json (in load order, later mods winning) and writes the result
/// into the game's tables in memory. Game files are never touched.
/// </summary>
[SupportedOSPlatform("windows")]
public class Startup : IMod
{
    private const string TablesFile = "TIC/tables.json";
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

    private IModLoader _modLoader = null!;
    private ILogger _logger = null!;

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
    {
        _modLoader = (IModLoader)loaderApi;
        _logger = (ILogger)_modLoader.GetLogger();
        _modLoader.OnModLoaderInitialized += OnAllModsLoaded;
    }

    private void OnAllModsLoaded()
    {
        var edits = CollectEdits();
        if (edits.IsEmpty)
        {
            Log("no enabled mod has " + TablesFile);
            return;
        }
        // The game may still be unpacking its data; don't hold up startup.
        new Thread(() => Apply(edits)) { IsBackground = true, Name = "TIC Loader" }.Start();
    }

    private TableEdits CollectEdits()
    {
        var merged = new TableEdits();
        foreach (var mod in _modLoader.GetActiveMods())
        {
            var id = mod.Generic.ModId;
            var path = Path.Combine(_modLoader.GetDirectoryForModId(id), TablesFile);
            if (!File.Exists(path))
                continue;
            try
            {
                var edits = TableEdits.Parse(File.ReadAllText(path));
                merged.Merge(edits);
                Log($"{id}: {edits.All.Count()} change(s)");
            }
            catch (Exception error) when (error is FormatException or IOException or System.Text.Json.JsonException)
            {
                // One broken mod shouldn't take the others down with it.
                Log($"{id}: skipped, {error.Message}");
            }
        }
        return merged;
    }

    private void Apply(TableEdits edits)
    {
        try
        {
            var module = Process.GetCurrentProcess().MainModule ?? throw new InvalidOperationException("no main module");
            var moduleStart = (long)module.BaseAddress;
            var moduleEnd = moduleStart + module.ModuleMemorySize;
            var memory = ProcessMemory.Current;
            var waited = Stopwatch.StartNew();
            while (true)
            {
                var found = FindJobTable(memory, moduleStart, moduleEnd);
                if (found.Count == 1)
                {
                    var (address, before) = found[0];
                    var after = (byte[])before.Clone();
                    var applied = edits.ApplyTo(Jobs.Schema.Name, after, Jobs.Schema.RecordSize);
                    var bytes = memory.WriteChanges(address, before, after);
                    Log($"Jobs: {applied} field(s) applied, {bytes} byte(s) changed at module +0x{address - moduleStart:X}");
                    return;
                }
                if (found.Count > 1)
                {
                    Log($"Jobs: found {found.Count} possible tables, not guessing; nothing changed");
                    return;
                }
                if (waited.Elapsed > GiveUpAfter)
                {
                    Log("Jobs: table not found; this game version may not be supported yet. Nothing changed.");
                    return;
                }
                Thread.Sleep(500);
            }
        }
        catch (Exception error)
        {
            Log("failed: " + error);
        }
    }

    private static List<(long Address, byte[] Table)> FindJobTable(ProcessMemory memory, long from, long to)
    {
        var length = Jobs.Schema.RecordSize * Jobs.Schema.RecordCount;
        var found = new List<(long, byte[])>();
        foreach (var (start, bytes) in memory.ContiguousBlocks(from, to))
            foreach (var offset in JobTableFingerprint.Find(bytes))
                found.Add((start + offset, bytes.AsSpan((int)offset, length).ToArray()));
        return found;
    }

    private void Log(string message) => _logger.WriteLine("[TIC Loader] " + message);

    public void Suspend() { }
    public void Resume() { }
    public void Unload() { }
    public bool CanUnload() => false;
    public bool CanSuspend() => false;
    public Action Disposing => () => { };
}
