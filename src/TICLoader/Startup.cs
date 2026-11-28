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
public unsafe class Startup : IMod
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
        WatchDecompression();
    }

    // Data files (battles, later sprites) are edited as the game unpacks them: the decompressor
    // is wrapped as early as possible, before the game loads anything.
    private void WatchDecompression()
    {
        try
        {
            DecompressHook.Install(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!);
        }
        catch (Exception error)
        {
            Log("couldn't watch the game's unpacking (battles will be edited in memory later instead): " + error);
        }
    }

    // The battles: every piece the game unpacks that holds part of the battle files gets the mods'
    // units before the game sees it, so every copy the game makes of them has the edits.
    // The original battles, read before the unpacking hook edits them (null if not read yet).
    private byte[]? _originalBattles;

    private void PatchBattlesOnLoad(TableEdits edits)
    {
        if (!DecompressHook.Installed || !edits.ChangedTables.Contains(Battles.Units.Name))
            return;
        try
        {
            // Read before the hook changes anything: afterwards, reading them goes through it too.
            var original = _originalBattles = VanillaData.ReadBattles(Process.GetCurrentProcess().MainModule!.FileName);
            var patcher = new LoadPatcher(original, BattleMemory.Wanted(original, edits), Battles.Units.RecordSize);
            if (!patcher.HasChanges)
                return;
            DecompressHook.Output += (address, size) =>
            {
                var replaced = patcher.Patch(new Span<byte>((void*)address, size));
                if (replaced > 0)
                    Log($"Battles: {replaced} unit record(s) changed as the game unpacked them (0x{address:X})");
            };
            Log("Battles: will be changed as the game unpacks them");
        }
        catch (Exception error)
        {
            Log("Battles: couldn't prepare changing them on load (they'll be changed in memory instead): " + error.Message);
        }
    }

    /// <summary>Folder in a mod holding replacement files: TIC/files/&lt;archive&gt;/&lt;path in it&gt;, e.g. TIC/files/0004/nxd/generaljob.nxd.</summary>
    private const string FilesFolder = "TIC/files";

    // Mods' replacement files for files packed in blocks: the same size is copied in as the block
    // unpacks; another size goes in a buffer of its own and the game's archive entry is pointed at it.
    private void ReplaceFilesOnLoad(List<(string Id, string Folder)> mods)
    {
        var wanted = new Dictionary<(string Archive, string Path), (string Mod, string File)>();
        foreach (var (id, folder) in mods)
        {
            var root = Path.Combine(folder, FilesFolder);
            if (!Directory.Exists(root))
                continue;
            foreach (var archiveFolder in Directory.GetDirectories(root))
                foreach (var file in Directory.GetFiles(archiveFolder, "*", SearchOption.AllDirectories))
                    wanted[(Path.GetFileName(archiveFolder), Path.GetRelativePath(archiveFolder, file).Replace('\\', '/'))] = (id, file); // later mods win
        }
        if (wanted.Count == 0)
            return;
        if (!DecompressHook.Installed)
        {
            Log($"files: {wanted.Count} replacement(s), but the game's unpacking isn't watched; not replaced");
            return;
        }
        var gameFolder = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
        var replacements = new List<TICPatcher.Core.Archives.BlockFileReplacement>();
        using var codec = new TICPatcher.Core.Archives.GDeflate(gameFolder);
        foreach (var group in wanted.GroupBy(w => w.Key.Archive))
        {
            var pac = Path.Combine(gameFolder, "data", "enhanced", group.Key + ".pac");
            if (!File.Exists(pac))
            {
                Log($"files: no archive {group.Key}.pac; {group.Count()} file(s) skipped");
                continue;
            }
            var archive = TICPatcher.Core.Archives.PacArchive.Open(pac);
            foreach (var ((_, path), (mod, file)) in group)
            {
                // The archives name their files without the folder the game asks for (nxd/x.nxd is x.nxd).
                if ((archive.Find(path) ?? archive.Find(Path.GetFileName(path))) is not { } entry)
                {
                    Log($"files: {mod}: {group.Key}/{path} isn't in the archive; skipped");
                    continue;
                }
                if (archive.ReadBlock(entry, codec) is not { } block)
                {
                    Log($"files: {mod}: {group.Key}/{path} isn't packed in a block; replacing such files isn't supported yet");
                    continue;
                }
                replacements.Add(new(group.Key, path, entry.Hash, block, entry.Offset, (int)entry.Size, File.ReadAllBytes(file)));
                Log($"files: {mod}: {group.Key}/{path} will be replaced ({entry.Size} -> {new FileInfo(file).Length} bytes)");
            }
        }
        if (replacements.Count == 0)
            return;

        // The game's archive entries for files that change size, found once (the game reads its
        // archives' headers at start-up).
        var memory = new ProcessMemory(new IntPtr(-1)); // its own scan buffer: other searches may run meanwhile
        var entries = new System.Collections.Concurrent.ConcurrentDictionary<ulong, long[]>();
        long[] FindEntries(TICPatcher.Core.Archives.BlockFileReplacement r)
        {
            if (entries.TryGetValue(r.Hash, out var known) && known.Length > 0)
                return known;
            var hash = BitConverter.GetBytes(r.Hash);
            var found = new List<long>();
            foreach (var (start, block) in memory.ScanBlocks(0, 0x7FFF_FFFF_FFFF, privateOnly: true))
                for (var at = block.AsSpan().IndexOf(hash); at >= 0;)
                {
                    if (at >= 0x28 && r.IsEntry(block.AsSpan(at - 0x28, Math.Min(0x38, block.Count - at + 0x28))))
                        found.Add(start + at - 0x28);
                    var next = block.AsSpan(at + 1).IndexOf(hash);
                    at = next < 0 ? -1 : at + 1 + next;
                }
            return entries[r.Hash] = [.. found];
        }
        var repointed = new HashSet<ulong>();
        DecompressHook.Output += (address, size) =>
        {
            var piece = new Span<byte>((void*)address, size);
            foreach (var r in replacements)
            {
                if (r.BlockStart(piece, address) is not { } blockStart)
                    continue;
                if (r.SameSize)
                {
                    if (r.PatchInPlace(piece, address, blockStart) > 0)
                        Log($"files: {r.Archive}/{r.Path} replaced as the game unpacked it");
                    continue;
                }
                lock (repointed)
                {
                    if (repointed.Contains(r.Hash))
                        continue;
                    var at = FindEntries(r);
                    if (at.Length == 0)
                    {
                        Log($"files: {r.Archive}/{r.Path}: the game's archive entry wasn't found; not replaced");
                        repointed.Add(r.Hash);
                        continue;
                    }
                    // Kept for the game's lifetime: the game reads the file from here.
                    var buffer = memory.Allocate(r.Replacement.Length);
                    memory.Write(buffer, r.Replacement);
                    foreach (var entry in at)
                    {
                        var bytes = memory.Read(entry, 0x38);
                        TICPatcher.Core.Archives.BlockFileReplacement.Repoint(bytes, blockStart, buffer, r.Replacement.Length);
                        memory.Write(entry, bytes);
                    }
                    repointed.Add(r.Hash);
                    Log($"files: {r.Archive}/{r.Path} ({r.Replacement.Length} bytes) given to the game from 0x{buffer:X} ({at.Length} entry/entries pointed at it)");
                }
            }
        };
        // Find the entries early, off the game's loading thread, where possible.
        new Thread(() =>
        {
            foreach (var r in replacements.Where(r => !r.SameSize))
                for (var tries = 0; tries < 200 && FindEntries(r).Length == 0; tries++)
                    Thread.Sleep(50);
        }) { IsBackground = true, Name = "TIC Loader files" }.Start();
    }

    private void OnAllModsLoaded()
    {
        var mods = _modLoader.GetActiveMods().Select(m => m.Generic.ModId).Select(id => (id, _modLoader.GetDirectoryForModId(id))).ToList();
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
        PatchBattlesOnLoad(edits);
        ReplaceFilesOnLoad(mods);
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
            using (var game = Process.GetCurrentProcess())
                Log($"memory at start: game {game.PrivateMemorySize64 >> 20} MB");
            _api.GameVersion = build?.Name;

            foreach (var table in edits.ChangedTables)
            {
                var schema = TableEdits.KnownTables[table];
                if (table == Battles.Units.Name)
                    new Thread(() => KeepBattles(memory, module.FileName, edits)) { IsBackground = true, Name = "TIC Loader battles" }.Start();
                else if (table == Jobs.Schema.Name)
                    ApplyTable(memory, schema, edits, moduleStart, jobs);
                else if (build is not null && build.Tables.ContainsKey(table))
                    foreach (var offset in build.OffsetsOf(table))
                        ApplyTable(memory, schema, edits, moduleStart, offset);
                else if (schema.InExecutable)
                    Log($"{table}: skipped, not located in this game version");
                // (Tables kept in game files, e.g. the job tree, come with the mod's files.)
            }
            _api.RaiseTablesApplied(Log);

            if (edits.AllText.Any())
                ApplyText(memory, edits);

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

    // Names and descriptions: the game loads its text tables on its own schedule,
    // so keep looking for a while until every table the mods change is done.
    private void ApplyText(ProcessMemory memory, TableEdits edits)
    {
        var tables = edits.AllText.Select(e => e.Table).Distinct().Count();
        var done = new TICPatcher.Core.Text.TextApplier.Done();
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var attempt = Stopwatch.StartNew();
            var count = TICPatcher.Core.Text.TextApplier.Relocate(memory, edits, done, Log, insideGame: true);
            Log($"text: looked in {attempt.ElapsedMilliseconds} ms, {count} of {tables} table(s) done");
            if (count >= tables)
            {
                Log($"text: all {tables} table(s) changed");
                Tidy("text");
                return;
            }
            if (waited.Elapsed > TimeSpan.FromMinutes(3))
            {
                Log($"text: only {done.Tables.Count} of {tables} table(s) were found; the rest is unchanged");
                Tidy("text");
                return;
            }
            Thread.Sleep(3000);
        }
    }

    // Battles: the game loads them into a block of its own memory at start-up. Find it
    // (by the original battles, read from the game's archive), write the mods' units, and
    // keep checking: if the game reloads its battles, write them again.
    private void KeepBattles(ProcessMemory memory, string gameExe, TableEdits edits)
    {
        try
        {
            // Not read again once the unpacking hook is on: it would hand back the edited battles.
            var original = _originalBattles ?? VanillaData.ReadBattles(gameExe);
            var wanted = BattleMemory.Wanted(original, edits);
            Log($"Battles: {edits.All.Count(e => e.Table == Battles.Units.Name)} field(s) to apply; looking for the game's battles");
            var told = false;
            var crashed = false;
            var lastReason = "";
            var waited = Stopwatch.StartNew();
            long? at = null;
            while (true)
            {
                if (at is { } known && BattleMemory.Holds(memory, known, wanted))
                {
                    Thread.Sleep(5000);
                    continue;
                }
                var attempt = Stopwatch.StartNew();
                try
                {
                    at = BattleMemory.Find(memory, original, insideGame: true, why: reason =>
                    {
                        lastReason = reason;
                        if (!told)
                            Log($"Battles: not found yet ({reason})");
                        told = true;
                    });
                }
                catch (Exception error)
                {
                    // One search failing (memory changing under it) isn't the end: try again.
                    at = null;
                    if (!crashed)
                        Log($"Battles: a search failed, trying again: {error.GetType().Name}: {error.Message}");
                    crashed = true;
                }
                if (at is { } found)
                {
                    try
                    {
                        var bytes = BattleMemory.Apply(memory, found, wanted);
                        Log($"Battles: {bytes} byte(s) changed at 0x{found:X} (looked for {attempt.ElapsedMilliseconds} ms)");
                        Tidy("battles");
                    }
                    catch (System.ComponentModel.Win32Exception error)
                    {
                        // The place went away (or was never whole): look again rather than stop.
                        Log($"Battles: couldn't write at 0x{found:X} ({error.Message}); looking again");
                        at = null;
                        Thread.Sleep(2000);
                    }
                    waited.Restart();
                }
                else if (waited.Elapsed > TimeSpan.FromMinutes(3))
                {
                    Log($"Battles: the game's battles weren't found ({lastReason}); battles are unchanged. Still looking every 30 s.");
                    Thread.Sleep(30000);
                    waited.Restart();
                }
                else
                    Thread.Sleep(2000);
            }
        }
        catch (Exception error)
        {
            Log("Battles: failed: " + error);
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

    // After a search of the game's memory: give the memory its reads used back to Windows (.NET
    // keeps a collected heap's memory otherwise), and say how much the game and TIC Loader use.
    private void Tidy(string after)
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        using var game = Process.GetCurrentProcess();
        Log($"memory after {after}: game {game.PrivateMemorySize64 >> 20} MB, of which TIC Loader's .NET heap {GC.GetTotalMemory(false) >> 20} MB ({GC.GetGCMemoryInfo().TotalCommittedBytes >> 20} MB committed)");
    }

    private void Log(string message) => _logger.WriteLine("[TIC Loader] " + message);

    public void Suspend() { }
    public void Resume() { }
    public void Unload() { }
    public bool CanUnload() => false;
    public bool CanSuspend() => false;
    public Action Disposing => () => { };
}
