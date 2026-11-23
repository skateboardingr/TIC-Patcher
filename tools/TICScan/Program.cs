using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

// TICScan: finds the job table in the running game, read-only.
//   TICScan <reference Jobs.bin> [process name or id, default fft_enhanced] [--all]
//           [--set <job id hex> <field> <value>]
// The reference is a copy of the original job table (e.g. FFTPatcher's
// PSP defaults); it is only compared against, never written anywhere.
// Without --all only the game's own executable image is searched.
// --set writes one field of one job (temporary: gone when the game closes).
// --dump <file> also saves the game's executable image as it is in memory,
// unreadable gaps zero-filled, so offsets in the file are module offsets.
// It's for offline research only; never share it (it is the game's code).

// TICScan --image <image.bin> <table> <id>... prints records, all fields,
// from a --dump file of a known game version. Works on any OS.
if (args.Length >= 4 && args[0] == "--image")
    return ImageViewer.Show(args[1], args[2], args[3..]);

// TICScan --gdeflate-test <dstorage folder>: does DirectStorage's codec work here?
if (args.Length >= 2 && args[0] == "--gdeflate-test" && OperatingSystem.IsWindows())
{
    foreach (var threads in new uint[] { 0, 1, 4 })
    {
        try
        {
            using var codec = new TICPatcher.Core.Archives.GDeflate(args[1], threads);
            var sample = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("The Ivalice Chronicles ", 5000)));
            var packed = codec.Compress(sample);
            var back = codec.Decompress(packed, sample.Length);
            Console.WriteLine($"threads {threads}: ok, {sample.Length} -> {packed.Length} -> {back.Length}, same: {back.AsSpan().SequenceEqual(sample)}, starts {Convert.ToHexString(packed, 0, 8)}");
        }
        catch (Exception error)
        {
            Console.WriteLine($"threads {threads}: {error.GetType().Name} {error.Message} (0x{error.HResult:X8})");
        }
    }
    return 0;
}

// TICScan --pac-list <archive.pac> [filter], and
// TICScan --pac-extract <archive.pac> <file name> <out file>: research helpers for the
// game's archives.
if (args.Length >= 2 && (args[0] == "--pac-list" || args[0] == "--pac-extract"))
{
    var archive = TICPatcher.Core.Archives.PacArchive.Open(args[1]);
    if (args[0] == "--pac-list")
    {
        Console.WriteLine($"{archive.Name}: {archive.Entries.Count} files");
        foreach (var e in archive.Entries.Where(e => args.Length < 3 || e.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"  {e.Name,-60} {e.Size,10}  {(e.Block != 0 ? "in block" : e.Compressed ? "compressed" : "stored")}");
        return 0;
    }
    if (!OperatingSystem.IsWindows() || args.Length < 4)
        return 2;
    // DirectStorage's CPU codec: the game's own DLLs (two folders up from data\enhanced), or a
    // dstorage folder next to TICScan.
    var gameFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(args[1]))))!;
    TICPatcher.Core.Archives.GDeflate codec;
    try
    {
        codec = new TICPatcher.Core.Archives.GDeflate(gameFolder);
        Console.WriteLine("using the game's DirectStorage");
    }
    catch (Exception error) when (error is NotImplementedException or DllNotFoundException or EntryPointNotFoundException)
    {
        codec = new TICPatcher.Core.Archives.GDeflate(Path.Combine(AppContext.BaseDirectory, "dstorage"));
        Console.WriteLine("using TICScan's DirectStorage");
    }
    using var _ = codec;
    var entry = archive.Find(args[2]) ?? throw new FileNotFoundException(args[2]);
    var bytes = archive.Read(entry, codec);
    File.WriteAllBytes(args[3], bytes);
    Console.WriteLine($"{entry.Name}: {bytes.Length} bytes to {args[3]}");
    return 0;
}

// TICScan --save-vanilla <FFT_enhanced.exe or memory image> <out.json>, and
// TICScan --compare <a.json> <b.json>: research helpers for the original tables.
if (args.Length >= 3 && args[0] == "--save-vanilla")
{
    var bytes = File.ReadAllBytes(args[1]);
    var looksLikeImage = bytes.Length > 0x1000 && BitConverter.ToUInt32(bytes, BitConverter.ToInt32(bytes, 0x3C) + 24 + 56) == bytes.Length;
    var data = TICPatcher.Core.Mods.VanillaData.FromImage(looksLikeImage ? bytes : TICPatcher.Core.Mods.VanillaData.ImageOf(bytes));
    data.Save(args[2]);
    Console.WriteLine($"saved {data.Tables.Count} tables from {(looksLikeImage ? "a memory image" : "an executable")}");
    return 0;
}
if (args.Length >= 3 && args[0] == "--compare")
{
    var a = TICPatcher.Core.Mods.VanillaData.Load(args[1]);
    var b = TICPatcher.Core.Mods.VanillaData.Load(args[2]);
    foreach (var (name, bytes) in a.Tables)
    {
        var other = b.Tables[name];
        var differ = Enumerable.Range(0, bytes.Length).Where(i => bytes[i] != other[i]).ToList();
        var size = AllTables.ByName[name].RecordSize;
        Console.WriteLine($"  {name,-18} {(differ.Count == 0 ? "identical" : $"{differ.Count} byte(s) differ, records {string.Join(" ", differ.Select(i => $"{i / size + AllTables.ByName[name].FirstId:X2}").Distinct().Take(10))}")}");
    }
    return 0;
}

// TICScan --exe <FFT_enhanced.exe>: can the original tables be read from the file on disk?
if (args.Length >= 2 && args[0] == "--exe")
{
    try
    {
        var vanilla = TICPatcher.Core.Mods.VanillaData.FromExecutable(args[1]);
        var squire = vanilla.Tables["Jobs"].AsSpan(0x4A * 49, 49).ToArray();
        Console.WriteLine($"Read from the file: {vanilla.BuildName}, {vanilla.Tables.Count} tables. Squire Move={RecordCodec.Read(Jobs.Schema.Field("Move"), squire)}, HPConstant={RecordCodec.Read(Jobs.Schema.Field("HPConstant"), squire)}");
        return 0;
    }
    catch (Exception error) when (error is FormatException or NotSupportedException or InvalidOperationException or IOException)
    {
        Console.WriteLine($"Can't read the tables from the file: {error.Message}");
        return 1;
    }
}

// TICScan --poke-text <hex address> <text>: overwrite text in the game's memory (temporary;
// research only). The text is written as-is, without a terminator.
if (args.Length >= 3 && args[0] == "--poke-text" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ
        | ProcessMemory.Native.PROCESS_VM_WRITE | ProcessMemory.Native.PROCESS_VM_OPERATION, false, game.Id);
    var memory = new ProcessMemory(handle);
    var address = Convert.ToInt64(args[1], 16);
    var text = System.Text.Encoding.UTF8.GetBytes(args[2]);
    var before = System.Text.Encoding.UTF8.GetString(memory.Read(address, text.Length));
    memory.Write(address, text);
    Console.WriteLine($"0x{address:X}: \"{before}\" -> \"{args[2]}\"");
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

// TICScan --memory-stats: the game's committed memory by type and protection (read-only).
if (args.Length >= 1 && args[0] == "--memory-stats" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    var stats = new Dictionary<string, (long Bytes, int Regions, long Largest)>();
    long address = 0;
    while (ProcessMemory.Native.VirtualQueryEx(handle, (IntPtr)address, out var info, (IntPtr)Marshal.SizeOf<ProcessMemory.Native.MEMORY_BASIC_INFORMATION>()) != IntPtr.Zero)
    {
        if (info.State == 0x1000)
        {
            var key = $"type 0x{info.Type:X5} protect 0x{info.Protect:X3}";
            var (b, r, l) = stats.GetValueOrDefault(key);
            stats[key] = (b + (long)info.RegionSize, r + 1, Math.Max(l, (long)info.RegionSize));
        }
        address = (long)info.BaseAddress + (long)info.RegionSize;
    }
    foreach (var (key, (bytes, regions, largest)) in stats.OrderByDescending(s => s.Value.Bytes))
        Console.WriteLine($"{key}: {bytes / 1024 / 1024,6} MB in {regions,5} regions, largest {largest / 1024 / 1024} MB");
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

// TICScan --pointers-to <hex address>[,<hex address>…] [span hex]: 8-byte values in the game's working
// memory that point at (or into the first <span> bytes of) those addresses (read-only).
if (args.Length >= 2 && args[0] == "--pointers-to" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var targets = args[1].Split(',').Select(t => Convert.ToInt64(t, 16)).ToArray();
    var span = args.Length > 2 ? Convert.ToInt64(args[2], 16) : 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    var memory = new ProcessMemory(handle);
    var module = game.MainModule!;
    foreach (var (start, bytes) in memory.ContiguousBlocks(0, 0x7FFF_FFFF_FFFF))
        for (var i = 0; i + 8 <= bytes.Length; i += 8)
        {
            var value = BitConverter.ToInt64(bytes, i);
            foreach (var target in targets)
                if (value >= target && value < target + span)
                {
                    var where = start + i;
                    var inExe = where >= (long)module.BaseAddress && where < (long)module.BaseAddress + module.ModuleMemorySize;
                    Console.WriteLine($"0x{where:X}{(inExe ? " (in the exe)" : "")} -> 0x{value:X} (target 0x{target:X} +0x{value - target:X})");
                }
        }
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

// TICScan --regions <hex address> <hex length>: the memory regions from there on (read-only).
if (args.Length >= 3 && args[0] == "--regions" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    long address = Convert.ToInt64(args[1], 16), end = address + Convert.ToInt64(args[2], 16);
    var states = new Dictionary<uint, string> { [0x1000] = "committed", [0x2000] = "reserved", [0x10000] = "FREE" };
    while (address < end && ProcessMemory.Native.VirtualQueryEx(handle, (IntPtr)address, out var info, (IntPtr)Marshal.SizeOf<ProcessMemory.Native.MEMORY_BASIC_INFORMATION>()) != IntPtr.Zero)
    {
        Console.WriteLine($"0x{(long)info.BaseAddress:X}  {(long)info.RegionSize / 1024,12} KB  {states.GetValueOrDefault(info.State, info.State.ToString("X"))}  type 0x{info.Type:X}  allocation base 0x{(long)info.AllocationBase:X}");
        address = (long)info.BaseAddress + (long)info.RegionSize;
    }
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

// TICScan --apply-text <tables.json>: apply a mod's text edits to the running game (temporary).
if (args.Length >= 2 && args[0] == "--apply-text" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var edits = TICPatcher.Core.Mods.TableEdits.Parse(File.ReadAllText(args[1]));
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ
        | ProcessMemory.Native.PROCESS_VM_WRITE | ProcessMemory.Native.PROCESS_VM_OPERATION, false, game.Id);
    var done = TICPatcher.Core.Text.TextApplier.Relocate(new ProcessMemory(handle), edits, new TICPatcher.Core.Text.TextApplier.Done(), Console.WriteLine);
    Console.WriteLine($"{done} text table(s) done");
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

// TICScan --text-tables: the text tables TIC Loader would find in the running game, and
// TICScan --game-text <FFT_enhanced.exe>: the original text read from the game's archives.
if (args.Length >= 1 && args[0] == "--text-tables" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    var memory = new ProcessMemory(handle);
    var watch = Stopwatch.StartNew();
    foreach (var (start, bytes) in memory.ContiguousBlocks(0, 0x7FFF_FFFF_FFFF))
        foreach (var found in TICPatcher.Core.Text.TextPatcher.Find(bytes, start))
        {
            var table = TICPatcher.Core.Text.NxdTable.Parse(bytes[(int)(found.Address - start)..]);
            var sample = found.Table.Name switch { "ItemText" => 0xFD, "JobText" => 0x4B, "AbilityText" => 0x01, _ => 0x1E };
            Console.WriteLine($"{found.Table.Name} at 0x{found.Address:X}: {found.Rows.Count} rows; 0x{sample:X2} = \"{table.Text(found.Table, sample, found.Table.Fields[0])}\"");
        }
    Console.WriteLine($"scanned in {watch.ElapsedMilliseconds} ms");
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}
// TICScan --fftpatcher-defaults <FFTPatcher folder> [vanilla.json]: lists FFTPatcher's default
// tables and, given TIC's originals, how many PSP default records match TIC's (checks the import mapping).
if (args.Length >= 2 && args[0] == "--fftpatcher-defaults")
{
    var defaults = TICPatcher.Core.Import.FftPatch.ReadDefaults(args[1]);
    foreach (var (name, bytes) in defaults.Where(d => d.Key.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || d.Key.EndsWith(".ENT", StringComparison.OrdinalIgnoreCase)).OrderBy(d => d.Key))
        Console.WriteLine($"  {name,-45} {bytes.Length,8}");
    if (args.Length >= 3)
        foreach (var (table, same, total) in TICPatcher.Core.Import.FftPatch.CompareWithTic(defaults, TICPatcher.Core.Mods.VanillaData.Load(args[2])))
            Console.WriteLine($"{table,-20} {same,4}/{total,-4} PSP records identical in TIC");
    return 0;
}

// TICScan --import-fftpatch <patch.fftpatch> <FFTPatcher folder> <vanilla.json>: what the editor's import makes of a patch.
if (args.Length >= 4 && args[0] == "--import-fftpatch")
{
    var project = new TICPatcher.Core.Mods.ModProject(TICPatcher.Core.Mods.VanillaData.Load(args[3]));
    var report = TICPatcher.Core.Import.FftPatch.Read(args[1]).ApplyTo(project, TICPatcher.Core.Import.FftPatch.ReadDefaults(args[2]));
    Console.WriteLine(report);
    Console.WriteLine(project.Edits.ToJson());
    return 0;
}

if (args.Length >= 2 && args[0] == "--game-text" && OperatingSystem.IsWindows())
{
    var text = TICPatcher.Core.Text.GameText.FromGame(args[1]);
    foreach (var (table, rows) in text.Tables)
        Console.WriteLine($"{table}: {rows.Count} rows, e.g. {string.Join(" | ", rows.Values.Skip(1).Take(3).Select(r => r.First().Value))}");
    return 0;
}

// TICScan --peek <hex address> <length> [out file]: read-only look at the game's memory.
if (args.Length >= 3 && args[0] == "--peek" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    var bytes = new ProcessMemory(handle).Read(Convert.ToInt64(args[1], 16), int.Parse(args[2]));
    ProcessMemory.Native.CloseHandle(handle);
    if (args.Length > 3)
        File.WriteAllBytes(args[3], bytes);
    else
        Console.WriteLine(Convert.ToHexString(bytes));
    return 0;
}

// TICScan --find <text> [--nxd]: where the running game holds some text (read-only),
// and with --nxd every NXDF table header with the first name in it.
// TICScan --maps <FFT_enhanced.exe> <out.json>: every battle map's name, size and tiles (research).
if (args.Length >= 3 && args[0] == "--maps" && OperatingSystem.IsWindows())
{
    var gameFolder = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
    var archive = TICPatcher.Core.Archives.PacArchive.Open(Path.Combine(gameFolder, "data", "enhanced", "0002.pac"));
    using var codec = new TICPatcher.Core.Archives.GDeflate(gameFolder);
    var text = TICPatcher.Core.Text.GameText.FromGame(args[1]);
    var maps = new List<object>();
    foreach (var map in TICPatcher.Core.Maps.MapTerrain.MapsIn(archive))
    {
        try
        {
            var terrain = TICPatcher.Core.Maps.MapTerrain.Read(archive, codec, map);
            maps.Add(new
            {
                map,
                name = text.Get(TICPatcher.Core.Text.TextTables.Places, TICPatcher.Core.Text.TextTables.PlaceOfMap(map), "Name"),
                terrain.Width,
                terrain.Length,
                lower = terrain.Lower.Select(t => new[] { t.Surface, t.Height, t.Depth, t.SlopeHeight, t.SlopeType }),
                upper = terrain.Upper.Select(t => new[] { t.Surface, t.Height, t.Depth, t.SlopeHeight, t.SlopeType }),
            });
        }
        catch (Exception error) when (error is FormatException or FileNotFoundException)
        {
            Console.WriteLine($"map {map}: {error.Message}");
        }
    }
    File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(maps));
    Console.WriteLine($"{maps.Count} maps to {args[2]}");
    return 0;
}

// TICScan --codec-function <FFT_enhanced.exe> <out image>: where DirectStorage's GDeflate decompress
// (the codec's function table, slot 4) lives: its DLL and offset, and that DLL's image for tools/Disasm.
if (args.Length >= 3 && args[0] == "--codec-function" && OperatingSystem.IsWindows())
{
    var folder = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
    using var codec = new TICPatcher.Core.Archives.GDeflate(folder);
    var function = (long)System.Runtime.InteropServices.Marshal.ReadIntPtr(codec.FunctionTable, TICPatcher.Core.Archives.GDeflate.DecompressSlot * IntPtr.Size);
    foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
    {
        var start = (long)module.BaseAddress;
        if (function < start || function >= start + module.ModuleMemorySize)
            continue;
        Console.WriteLine($"decompress at {module.ModuleName} +0x{function - start:X} ({module.FileName})");
        File.WriteAllBytes(args[2], TICPatcher.Core.Mods.VanillaData.ImageOf(File.ReadAllBytes(module.FileName!)));
        Console.WriteLine($"image of {module.ModuleName} written to {args[2]}");
    }
    return 0;
}

// TICScan --pac-grep <archive.pac> <text>: which files in an archive contain some text (UTF-8).
if (args.Length >= 3 && args[0] == "--pac-grep" && OperatingSystem.IsWindows())
{
    var archive = TICPatcher.Core.Archives.PacArchive.Open(args[1]);
    var gameFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(args[1]))))!;
    using var codec = new TICPatcher.Core.Archives.GDeflate(gameFolder);
    var needle = System.Text.Encoding.UTF8.GetBytes(args[2]);
    foreach (var entry in archive.Entries)
    {
        try
        {
            var bytes = archive.Read(entry, codec);
            var at = bytes.AsSpan().IndexOf(needle);
            if (at >= 0)
                Console.WriteLine($"  {entry.Name} at 0x{at:X}");
        }
        catch (Exception error) when (error is InvalidDataException or IOException or System.Runtime.InteropServices.COMException)
        {
            Console.WriteLine($"  {entry.Name}: couldn't read ({error.Message})");
        }
    }
    return 0;
}

// TICScan --active-battle <battle hex> [battles to look for, hex, e.g. 100-10F 180]: which battles' units the running game has copied out of its
// battle block (e.g. for the scene on screen), and whether one battle in the block holds edits. Read-only.
if (args.Length >= 1 && args[0] == "--active-battle" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    var memory = new ProcessMemory(handle);
    var original = TICPatcher.Core.Mods.VanillaData.ReadBattles(game.MainModule!.FileName);
    var block = TICPatcher.Core.Mods.BattleMemory.Find(memory, original, why: reason => Console.WriteLine($"battle block not found: {reason}"));
    if (block is { } b)
        Console.WriteLine($"battle block at 0x{b:X}");
    byte[]? current = block is { } at ? memory.Read(at, original.Length) : null;
    if (current is not null && args.Length > 1)
    {
        var battle = Convert.ToInt32(args[1], 16);
        var changed = Enumerable.Range(0, 16).Where(u => !current.AsSpan(battle * 640 + u * 40, 40).SequenceEqual(original.AsSpan(battle * 640 + u * 40, 40))).ToList();
        Console.WriteLine($"battle {battle:X3} in the block: {(changed.Count == 0 ? "original" : $"units {string.Join(", ", changed.Select(u => u + 1))} changed")}");
    }
    // Distinctive unit records (non-empty, appearing once in all battles), searched outside the block.
    var records = new Dictionary<string, (int Battle, int Unit)>();
    var seen = new Dictionary<string, int>();
    var source = current ?? original;
    for (var r = 0; r < source.Length / 40; r++)
    {
        var record = source.AsSpan(r * 40, 40);
        if (record[0] == 0)
            continue;
        var key = Convert.ToHexString(record);
        seen[key] = seen.GetValueOrDefault(key) + 1;
        records[key] = (r / 16, r % 16);
    }
    // Only the battles asked about (each "A" or "A-B"), or the edited one: searching for all is far too slow.
    var wanted = new HashSet<int>();
    foreach (var range in args.Skip(2).DefaultIfEmpty(args.Length > 1 ? args[1] : "0"))
    {
        var ends = range.Split('-').Select(x => Convert.ToInt32(x, 16)).ToArray();
        for (var x = ends[0]; x <= ends[^1]; x++)
            wanted.Add(x);
    }
    var probes = records.Where(r => seen[r.Key] == 1 && wanted.Contains(r.Value.Battle))
        .Select(r => (Bytes: Convert.FromHexString(r.Key), r.Value.Battle, r.Value.Unit)).ToList();
    Console.WriteLine($"looking for {probes.Count} unit record(s) of {wanted.Count} battle(s)");
    var hits = new Dictionary<int, HashSet<int>>();
    foreach (var (start, bytes) in memory.ContiguousBlocks(0, long.MaxValue, privateOnly: true))
        foreach (var (probe, battle, unit) in probes)
        {
            for (var i = bytes.AsSpan().IndexOf(probe); i >= 0;)
            {
                var address = start + i;
                if (block is not { } blk || address < blk || address >= blk + original.Length)
                    (hits.TryGetValue(battle, out var units) ? units : hits[battle] = []).Add(unit + 1);
                var next = bytes.AsSpan(i + 1).IndexOf(probe);
                i = next < 0 ? -1 : i + 1 + next;
            }
        }
    foreach (var (battle, units) in hits.OrderByDescending(h => h.Value.Count).Take(10))
        Console.WriteLine($"  battle {battle:X3}: {units.Count} unit record(s) copied elsewhere (units {string.Join(", ", units.Order())})");
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

// TICScan --battle-copies: every copy of the battle files in the running game's memory, original or
// edited, how much of each matches, and whether it sits inside a whole unpacked archive block. Read-only.
if (args.Length >= 1 && args[0] == "--battle-copies" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    var memory = new ProcessMemory(handle);
    var exe = game.MainModule!.FileName;
    var original = TICPatcher.Core.Mods.VanillaData.ReadBattles(exe);
    // The bytes just before the battle files in their unpacked block, to tell block copies from file copies.
    var archive = TICPatcher.Core.Archives.PacArchive.Open(Path.Combine(Path.GetDirectoryName(exe)!, "data", "enhanced", "0002.pac"));
    using var codec = new TICPatcher.Core.Archives.GDeflate(Path.GetDirectoryName(exe)!);
    var first = archive.Find(TICPatcher.Core.Tables.Schemas.Battles.Files[0])!;
    var before = archive.Read(archive.Entries.Where(e => e.Block == first.Block && e.Offset < first.Offset).OrderByDescending(e => e.Offset).First(), codec);
    var lead = before.AsSpan(Math.Max(0, before.Length - 64)).ToArray();
    // Battle 1's first unit marks each copy.
    var probe = original.AsSpan(640, 40).ToArray();
    var copies = new List<long>();
    foreach (var (start, bytes) in memory.ContiguousBlocks(0, long.MaxValue, privateOnly: true))
        for (var i = bytes.AsSpan().IndexOf(probe); i >= 0;)
        {
            copies.Add(start + i - 640);
            var next = bytes.AsSpan(i + 1).IndexOf(probe);
            i = next < 0 ? -1 : i + 1 + next;
        }
    Console.WriteLine($"{copies.Count} copies of battle 1's first unit");
    var stats = new Dictionary<string, int>();
    foreach (var copy in copies)
    {
        byte[] bytes;
        try { bytes = memory.Read(copy, original.Length); } catch { continue; }
        var same = 0;
        for (var r = 0; r < original.Length / 40; r++)
            if (bytes.AsSpan(r * 40, 40).SequenceEqual(original.AsSpan(r * 40, 40)))
                same++;
        var whole = same == original.Length / 40 ? "all original" : $"{original.Length / 40 - same} records differ";
        string inBlock;
        try { inBlock = memory.Read(copy - lead.Length, lead.Length).AsSpan().SequenceEqual(lead) ? "inside an unpacked block" : "on its own"; }
        catch { inBlock = "start of a region"; }
        var key = $"{whole}, {inBlock}";
        stats[key] = stats.GetValueOrDefault(key) + 1;
        if (stats[key] <= 2)
            Console.WriteLine($"  0x{copy:X}: {key}");
    }
    foreach (var (key, count) in stats)
        Console.WriteLine($"{count} x {key}");
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

// TICScan --battles: where the running game holds its battles (as TIC Loader finds them), read-only.
if (args.Length >= 1 && args[0] == "--battles" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    var memory = new ProcessMemory(handle);
    var original = TICPatcher.Core.Mods.VanillaData.ReadBattles(game.MainModule!.FileName);
    var timer = Stopwatch.StartNew();
    var at = TICPatcher.Core.Mods.BattleMemory.Find(memory, original);
    Console.WriteLine(at is { } found
        ? $"battles at 0x{found:X} ({timer.ElapsedMilliseconds} ms); {(TICPatcher.Core.Mods.BattleMemory.Holds(memory, found, original) ? "all original" : "some changed")}"
        : $"not found ({timer.ElapsedMilliseconds} ms)");
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

// TICScan --find-file <file> [chunk size]: where pieces of a data file (e.g. a battle file
// extracted with --pac-extract) are in the running game's memory, and how much of the file
// surrounds each place, i.e. whether the game holds the whole file or just a part.
if (args.Length >= 2 && args[0] == "--find-file" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    var file = File.ReadAllBytes(args[1]);
    var chunk = args.Length > 2 ? int.Parse(args[2]) : 640;
    var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
    var memory = new ProcessMemory(handle);
    // Distinct, non-blank pieces of the file to look for.
    var probes = Enumerable.Range(0, file.Length / chunk)
        .Select(i => (Offset: i * chunk, Bytes: file.AsSpan(i * chunk, Math.Min(64, chunk)).ToArray()))
        .Where(p => p.Bytes.Distinct().Count() > 8)
        .GroupBy(p => Convert.ToHexString(p.Bytes)).Where(g => g.Count() == 1).Select(g => g.First())
        .ToList();
    Console.WriteLine($"{args[1]}: {file.Length} bytes, {probes.Count} distinct {chunk}-byte pieces to look for");
    var hits = new List<(long Address, int Offset)>();
    foreach (var (blockStart, bytes) in memory.ContiguousBlocks(0, long.MaxValue))
        foreach (var (offset, probe) in probes)
            for (var at = bytes.AsSpan().IndexOf(probe); at >= 0;)
            {
                hits.Add((blockStart + at, offset));
                var next = bytes.AsSpan(at + 1).IndexOf(probe);
                at = next < 0 ? -1 : at + 1 + next;
            }
    // Group hits that belong to one copy (same address minus offset).
    foreach (var copy in hits.GroupBy(h => h.Address - h.Offset).OrderByDescending(g => g.Count()).Take(20))
    {
        var start = copy.Key;
        var first = copy.Min(h => h.Offset);
        var last = copy.Max(h => h.Offset) + chunk;
        Console.WriteLine($"  copy at 0x{start:X} (file offset 0 there): {copy.Count()} piece(s), file bytes 0x{first:X}-0x{last:X}");
    }
    Console.WriteLine($"{hits.Count} hit(s) in all.");
    ProcessMemory.Native.CloseHandle(handle);
    return 0;
}

if (args.Length >= 2 && args[0] == "--find" && OperatingSystem.IsWindows())
{
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    if (game is null)
        return 1;
    return Finder.Run(game, System.Text.Encoding.UTF8.GetBytes(args[1].Replace("\\0", "\0")), args.Contains("--nxd"));
}

// TICScan --copies <table> [record id hex] [bytes]: finds copies of a table elsewhere
// in the running game's memory (e.g. one the game builds at run time), by its first
// bytes; with a record id, finds copies of just that record's first <bytes> bytes.
if (args.Length >= 2 && args[0] == "--copies")
{
    if (!OperatingSystem.IsWindows())
        return 2;
    var game = Process.GetProcessesByName("fft_enhanced").FirstOrDefault();
    int? record = args.Length > 2 ? Convert.ToInt32(args[2], 16) : null;
    var length = args.Length > 3 ? int.Parse(args[3]) : 0;
    return game is null ? 1 : Copies.Find(game, args[1], record, length);
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("TICScan reads another process's memory and needs Windows.");
    return 2;
}
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: TICScan <reference Jobs.bin> [process name or id] [--all] [--set <job> <field> <value>]");
    return 2;
}

var reference = File.ReadAllBytes(args[0]);
var schema = Jobs.Schema;
if (reference.Length != schema.RecordSize * schema.RecordCount)
{
    Console.Error.WriteLine($"Expected {schema.RecordSize * schema.RecordCount} bytes of reference, got {reference.Length}.");
    return 2;
}

var setIndex = Array.IndexOf(args, "--set");
(int Job, string Field, string Value)? set = setIndex >= 0 && args.Length > setIndex + 3
    ? (Convert.ToInt32(args[setIndex + 1], 16), args[setIndex + 2], args[setIndex + 3])
    : null;
var dumpIndex = Array.IndexOf(args, "--dump");
var dump = dumpIndex >= 0 && args.Length > dumpIndex + 1 ? args[dumpIndex + 1] : null;
var positional = args.Skip(1).Take(setIndex >= 0 ? setIndex - 1 : int.MaxValue).Where(a => !a.StartsWith("--") && a != dump);
var processName = positional.FirstOrDefault() ?? "fft_enhanced";
var all = args.Contains("--all");

var process = int.TryParse(processName, out var pid)
    ? Process.GetProcessById(pid)
    : Process.GetProcessesByName(processName).FirstOrDefault();
if (process is null)
{
    Console.Error.WriteLine($"{processName} isn't running.");
    return 1;
}
return Scanner.Run(process, reference, all, set, dump);

[SupportedOSPlatform("windows")]
static class Scanner
{
    public static int Run(Process process, byte[] reference, bool all, (int Job, string Field, string Value)? set, string? dump)
    {
        var module = process.MainModule ?? throw new InvalidOperationException("no main module");
        var moduleStart = (long)module.BaseAddress;
        var moduleEnd = moduleStart + module.ModuleMemorySize;
        Console.WriteLine($"{process.ProcessName} pid {process.Id}: {module.ModuleName} at 0x{moduleStart:X}, {module.ModuleMemorySize / 1024 / 1024} MB");

        var access = ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ
            | (set is null ? 0 : ProcessMemory.Native.PROCESS_VM_WRITE | ProcessMemory.Native.PROCESS_VM_OPERATION);
        var handle = ProcessMemory.Native.OpenProcess(access, false, process.Id);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess");
        try
        {
            var memory = new ProcessMemory(handle);
            if (dump is not null)
            {
                var image = new byte[module.ModuleMemorySize];
                foreach (var (start, bytes) in memory.ContiguousBlocks(moduleStart, moduleEnd))
                    bytes.AsSpan(0, (int)Math.Min(bytes.Length, moduleEnd - start)).CopyTo(image.AsSpan((int)(start - moduleStart)));
                File.WriteAllBytes(dump, image);
                Console.WriteLine($"  saved {image.Length / 1024 / 1024} MB to {dump}");
            }
            var locator = TableLocator.ForJobs(reference);
            var found = 0;
            foreach (var (start, bytes) in memory.ContiguousBlocks(all ? 0 : moduleStart, all ? long.MaxValue : moduleEnd))
            {
                foreach (var match in locator.Find(bytes).Take(5))
                {
                    found++;
                    var address = start + match.Offset;
                    var where = address >= moduleStart && address < moduleEnd ? $" (module +0x{address - moduleStart:X})" : "";
                    Console.WriteLine($"  candidate 0x{address:X}{where}: record size {match.RecordSize}, score {match.Score:P1}");
                    Describe(bytes, match, Jobs.FirstGenericJob, "Squire");
                    Describe(bytes, match, Jobs.FirstGenericJob + 1, "Chemist");
                    var skillSets = Enumerable.Range(Jobs.FirstGenericJob, Jobs.LastGenericJob - Jobs.FirstGenericJob + 1)
                        .Select(j => bytes[match.Offset + (long)j * match.RecordSize]);
                    Console.WriteLine($"    generic jobs' skill sets: {string.Join(" ", skillSets)}");
                    for (var ramza = 1; ramza <= 3; ramza++)
                        Describe(bytes, match, ramza, $"Ramza {ramza}");
                    if (set is { } s && found == 1)
                        Write(memory, address, bytes, match, s);
                }
            }
            Console.WriteLine(found == 0 ? "No candidates. Try --all." : $"{found} candidate(s).");
            return found == 0 ? 1 : 0;
        }
        finally
        {
            ProcessMemory.Native.CloseHandle(handle);
        }
    }

    private static void Write(ProcessMemory memory, long tableAddress, byte[] bytes, LocatorMatch match, (int Job, string Field, string Value) set)
    {
        var field = Jobs.Schema.Field(set.Field);
        var recordOffset = (int)(match.Offset + (long)set.Job * match.RecordSize);
        var record = bytes.AsSpan(recordOffset, Jobs.Schema.RecordSize).ToArray();
        var before = RecordCodec.Read(field, record);
        RecordCodec.Write(field, record, set.Value);
        var target = tableAddress + (long)set.Job * match.RecordSize + field.Offset;
        memory.Write(target, record[field.Offset..(field.Offset + field.Size)]);
        Console.WriteLine($"  set job 0x{set.Job:X2} {set.Field}: {before} -> {RecordCodec.Read(field, record)} (at 0x{target:X})");
    }

    // A found record decoded with the reference layout, to eyeball against the game.
    private static void Describe(byte[] bytes, LocatorMatch match, int job, string label)
    {
        var record = bytes.AsSpan((int)(match.Offset + (long)job * match.RecordSize), Jobs.Schema.RecordSize).ToArray();
        var fields = new[] { "HPConstant", "HPMultiplier", "PAConstant", "PAMultiplier", "Move", "Jump", "CEvade", "Equipment" };
        Console.WriteLine($"    {label} (0x{job:X2}): " + string.Join(", ", fields.Select(f => $"{f}={RecordCodec.Read(Jobs.Schema.Field(f), record)}")));
    }
}

static class ImageViewer
{
    public static int Show(string path, string table, string[] ids)
    {
        var image = File.ReadAllBytes(path);
        var build = GameBuilds.Identify(image);
        if (build is null)
        {
            Console.Error.WriteLine("Unknown game version.");
            return 1;
        }
        if (!AllTables.ByName.TryGetValue(table, out var schema) || !build.Tables.TryGetValue(table, out var offset))
        {
            Console.Error.WriteLine($"Unknown table {table}. Tables: {string.Join(", ", build.Tables.Keys)}");
            return 2;
        }
        Console.WriteLine($"{build.Name}: {table} at +0x{offset:X}");
        foreach (var text in ids)
        {
            var id = Convert.ToInt32(text, 16);
            var record = image.AsSpan((int)(offset + (long)(id - schema.FirstId) * schema.RecordSize), schema.RecordSize).ToArray();
            Console.WriteLine($"  0x{id:X2}: " + string.Join("; ", schema.Fields.Select(f => $"{f.Name}={RecordCodec.Read(f, record)}")));
        }
        return 0;
    }
}

[SupportedOSPlatform("windows")]
static class Copies
{
    public static int Find(Process game, string table, int? record = null, int length = 0)
    {
        var module = game.MainModule!;
        var start = (long)module.BaseAddress;
        var end = start + module.ModuleMemorySize;
        var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
        try
        {
            var memory = new ProcessMemory(handle);
            var build = GameBuilds.Identify(memory.Read(start, 0x1000)) ?? throw new NotSupportedException("unknown game version");
            var schema = AllTables.ByName[table];
            var original = memory.Read(start + build.Tables[table], schema.Length);
            // Skip the first records (often blank); look for a distinctive run.
            var probe = original.AsSpan(schema.RecordSize * 1, Math.Min(96, schema.Length - schema.RecordSize)).ToArray();
            if (record is { } id)
            {
                probe = original.AsSpan((id - schema.FirstId) * schema.RecordSize, length > 0 ? length : schema.RecordSize).ToArray();
                Console.WriteLine($"record 0x{id:X2}, first {probe.Length} bytes: {Convert.ToHexString(probe)}");
            }
            Console.WriteLine($"{table}: original at module +0x{build.Tables[table]:X}; searching all memory for copies");
            var found = 0;
            foreach (var (blockStart, bytes) in memory.ContiguousBlocks(0, long.MaxValue))
            {
                var span = bytes.AsSpan();
                for (var at = span.IndexOf(probe); at >= 0; )
                {
                    var copy = blockStart + at - schema.RecordSize;
                    var where = copy >= start && copy < end ? $"module +0x{copy - start:X}" : "outside the module";
                    var same = at - schema.RecordSize >= 0 && at - schema.RecordSize + schema.Length <= span.Length
                        ? span.Slice(at - schema.RecordSize, schema.Length).SequenceEqual(original) ? "identical" : "differs further on"
                        : "cut off";
                    Console.WriteLine($"  0x{copy:X} ({where}), {same}");
                    found++;
                    var next = span[(at + 1)..].IndexOf(probe);
                    at = next < 0 ? -1 : at + 1 + next;
                }
            }
            Console.WriteLine($"{found} place(s).");
            return 0;
        }
        finally
        {
            ProcessMemory.Native.CloseHandle(handle);
        }
    }
}

[SupportedOSPlatform("windows")]
static class Finder
{
    public static int Run(Process game, byte[] needle, bool nxd)
    {
        var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
        try
        {
            var memory = new ProcessMemory(handle);
            var module = game.MainModule!;
            long start = (long)module.BaseAddress, end = start + module.ModuleMemorySize;
            var magic = "NXDF"u8.ToArray();
            int hits = 0, tables = 0;
            foreach (var (blockStart, bytes) in memory.ContiguousBlocks(0, 0x7FFF_FFFF_FFFF))
            {
                var span = bytes.AsSpan();
                for (int at = span.IndexOf(needle), from = 0; at >= 0; from = at + 1, at = span[from..].IndexOf(needle) is var n and >= 0 ? from + n : -1)
                {
                    var address = blockStart + at;
                    Console.WriteLine($"text at 0x{address:X}{(address >= start && address < end ? " (in the exe)" : "")}");
                    hits++;
                }
                if (!nxd)
                    continue;
                for (int at = span.IndexOf(magic), from = 0; at >= 0; from = at + 1, at = span[from..].IndexOf(magic) is var n and >= 0 ? from + n : -1)
                {
                    if (at + 0x40 > span.Length || BitConverter.ToUInt32(span[(at + 0x20)..]) != 0x30)
                        continue;
                    var count = BitConverter.ToUInt32(span[(at + 0x24)..]);
                    tables++;
                    if (tables <= 80)
                        Console.WriteLine($"NXDF at 0x{blockStart + at:X}: {count} rows");
                }
            }
            Console.WriteLine($"{hits} text hit(s), {tables} NXDF table(s)");
            return 0;
        }
        finally
        {
            ProcessMemory.Native.CloseHandle(handle);
        }
    }
}
