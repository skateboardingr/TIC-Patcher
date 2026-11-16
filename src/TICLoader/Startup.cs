using System.Diagnostics;
using System.Runtime.Versioning;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using TICLoader.Interfaces;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Loader;

/// <summary>
/// TIC Loader: once every mod has loaded, merges each enabled mod's edits
/// (TIC/tables.json, formats other mods register, edits added from code; in
/// load order, later mods winning) and writes the result into the game's
/// tables in memory. Game files are never touched. Other mods reach it through
/// <see cref="ITICLoader"/> (docs/bridge.md).
/// </summary>
[SupportedOSPlatform("windows")]
public class Startup : IMod
{
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

    private IModLoader _modLoader = null!;
    private ILogger _logger = null!;
    private IModConfigV1 _config = null!;
    private readonly ModCollector _collector = new();
    private LoaderApi _api = null!;

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
    {
        _modLoader = (IModLoader)loaderApi;
        _logger = (ILogger)_modLoader.GetLogger();
        _config = modConfig;
        _api = new LoaderApi(_collector);
        _modLoader.AddOrReplaceController<ITICLoader>(this, _api);
        _modLoader.OnModLoaderInitialized += OnAllModsLoaded;
    }

    private void OnAllModsLoaded()
    {
        var mods = _modLoader.GetActiveMods().Select(m => m.Generic.ModId).Select(id => (id, _modLoader.GetDirectoryForModId(id)));
        var edits = _collector.Collect(mods, Log);
        ReadWatcher.Config? watch = null;
        try
        {
            watch = ReadWatcher.Load(OwnFolder);
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException)
        {
            Log("watch.json: " + error.Message);
        }
        if (edits.IsEmpty)
            Log("no enabled mod has table edits");
        if (edits.IsEmpty && watch is null)
            return;
        // The game may still be unpacking its data; don't hold up startup.
        new Thread(() => Apply(edits, watch)) { IsBackground = true, Name = "TIC Loader" }.Start();
    }

    private string OwnFolder => _modLoader.GetDirectoryForModId(_config.ModId);

    private void Apply(TableEdits edits, ReadWatcher.Config? watch)
    {
        try
        {
            var module = Process.GetCurrentProcess().MainModule ?? throw new InvalidOperationException("no main module");
            var moduleStart = (long)module.BaseAddress;
            var moduleEnd = moduleStart + module.ModuleMemorySize;
            var memory = ProcessMemory.Current;

            // The job table is found by its fingerprint, which needs no stored
            // offsets; finding it also tells us the game's data is unpacked.
            var waited = Stopwatch.StartNew();
            List<(long Address, byte[] Table)> found;
            while ((found = FindJobTable(memory, moduleStart, moduleEnd)).Count == 0)
            {
                if (waited.Elapsed > GiveUpAfter)
                {
                    Log("Jobs: table not found; this game version may not be supported yet. Nothing changed.");
                    return;
                }
                Thread.Sleep(500);
            }
            if (found.Count > 1)
            {
                Log($"Jobs: found {found.Count} possible tables, not guessing; nothing changed");
                return;
            }
            var jobs = found[0].Address - moduleStart;

            // Other tables need this exact game version's offsets, and the job
            // table must be where that version says, or nothing else is touched.
            var build = GameBuilds.Identify(memory.Read(moduleStart, 0x1000));
            if (build is not null && build.Tables[Jobs.Schema.Name] != jobs)
            {
                Log($"{build.Name}: job table isn't where expected (+0x{jobs:X}); only editing jobs");
                build = null;
            }
            Log(build is null ? "unknown game version: only jobs can be edited" : $"game version: {build.Name}");
            _api.GameVersion = build?.Name;

            foreach (var table in edits.ChangedTables)
            {
                var schema = TableEdits.KnownTables[table];
                if (table == Jobs.Schema.Name)
                    ApplyTable(memory, schema, edits, moduleStart, jobs);
                else if (build is not null && build.Tables.TryGetValue(table, out var offset))
                    ApplyTable(memory, schema, edits, moduleStart, offset);
                else
                    Log($"{table}: skipped, not located in this game version");
            }
            _api.RaiseTablesApplied(Log);

            if (watch is not null)
            {
                if (build is null)
                    Log("watch: needs a known game version; not watching");
                else
                    ReadWatcher.Start(watch, build.Tables, moduleStart, moduleEnd, Path.Combine(OwnFolder, "watch-report.txt"), Log);
            }
        }
        catch (Exception error)
        {
            Log("failed: " + error);
        }
    }

    private void ApplyTable(ProcessMemory memory, TableSchema schema, TableEdits edits, long moduleStart, long offset)
    {
        var address = moduleStart + offset;
        var before = memory.Read(address, schema.Length);
        var after = (byte[])before.Clone();
        var applied = edits.ApplyTo(schema.Name, after, schema.RecordSize);
        var bytes = memory.WriteChanges(address, before, after);
        Log($"{schema.Name}: {applied} field(s) applied, {bytes} byte(s) changed at module +0x{offset:X}");
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
