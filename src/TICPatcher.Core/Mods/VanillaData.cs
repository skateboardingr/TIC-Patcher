using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Mods;

/// <summary>
/// The game's original tables, copied from the player's own running game so
/// TIC Patcher never ships game data. Saved per game version in the player's
/// local app data.
/// </summary>
public sealed class VanillaData
{
    public required string BuildName { get; init; }
    public required uint TimeDateStamp { get; init; }
    public required uint SizeOfImage { get; init; }
    public required Dictionary<string, byte[]> Tables { get; init; }

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TICPatcher");

    public string DefaultPath => Path.Combine(Folder, $"vanilla-{TimeDateStamp:X8}-{SizeOfImage:X}.json");

    /// <summary>Every vanilla copy saved on this PC, newest first.</summary>
    public static IEnumerable<string> Saved() =>
        Directory.Exists(Folder)
            ? new DirectoryInfo(Folder).GetFiles("vanilla-*.json").OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName)
            : [];

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    public static VanillaData Load(string path)
    {
        var data = JsonSerializer.Deserialize<VanillaData>(File.ReadAllText(path))
            ?? throw new FormatException($"{path} is empty");
        foreach (var (name, bytes) in data.Tables)
        {
            if (AllTables.ByName.TryGetValue(name, out var schema) && bytes.Length != schema.Length)
                throw new FormatException($"{path}: {name} has {bytes.Length} bytes, expected {schema.Length}");
        }
        return data;
    }

    /// <summary>
    /// Adds the battles (ENTD), which aren't in the executable: read from the
    /// installed game's data\enhanced\0002.pac (read-only).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void AddBattles(string gameExe) => Tables[Battles.Units.Name] = ReadBattles(gameExe);

    /// <summary>
    /// Adds the unit sprite palettes (<see cref="SpriteSheets.Palettes"/>): the first 0x200 bytes
    /// of each battle sprite sheet in the installed game's data\enhanced\0002.pac (read-only).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void AddSpritePalettes(string gameExe)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(gameExe))!;
        var archive = Archives.PacArchive.Open(Path.Combine(folder, "data", "enhanced", "0002.pac"));
        using var codec = new Archives.GDeflate(folder);
        var files = Enumerable.Range(1, SpriteSheets.Count)
            .Select(id => (Id: id, Entry: Sprites.UnitSprite.FileOf(id) is { } name ? archive.Find(name) : null))
            .ToList();
        var sheets = archive.ReadMany(files.Where(f => f.Entry is not null).Select(f => f.Entry!), codec);
        var bytes = new byte[SpriteSheets.Palettes.Length];
        foreach (var (id, entry) in files)
            if (entry is not null && sheets[entry].Length >= 0x200)
                sheets[entry].AsSpan(0, 0x200).CopyTo(bytes.AsSpan((id - 1) * 0x200));
        Tables[SpriteSheets.Palettes.Name] = bytes;
    }

    /// <summary>The original battles, from the installed game's data\enhanced\0002.pac.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static byte[] ReadBattles(string gameExe)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(gameExe))!;
        var archive = Archives.PacArchive.Open(Path.Combine(folder, "data", "enhanced", "0002.pac"));
        using var codec = new Archives.GDeflate(folder);
        // Straight into one array, wiping the pieces: TIC Loader searches the game's memory
        // for these bytes and must find only the game's copy.
        var bytes = new byte[Battles.Units.Length];
        var at = 0;
        foreach (var file in Battles.Files)
        {
            var piece = archive.Read(archive.Find(file) ?? throw new FormatException($"{file} isn't in 0002.pac"), codec, wipe: true);
            if (at + piece.Length > bytes.Length)
                throw new FormatException($"The battle files hold more than {bytes.Length} bytes.");
            piece.CopyTo(bytes, at);
            at += piece.Length;
            Array.Clear(piece);
        }
        if (at != bytes.Length)
            throw new FormatException($"The battle files hold {at} bytes, expected {bytes.Length}.");
        return bytes;
    }

    /// <summary>
    /// Copies the tables out of an image of the game's module (its memory
    /// from the module start, as TICScan --dump writes it).
    /// </summary>
    public static VanillaData FromImage(byte[] image) =>
        FromReader(image.AsSpan(0, Math.Min(image.Length, 0x1000)), (offset, length) => image.AsSpan((int)offset, length).ToArray());

    /// <summary>
    /// Copies the tables out of the game's executable on disk (read-only), laid
    /// out as it is in memory. Throws if it isn't a known version or the tables
    /// can't be read from the file.
    /// </summary>
    public static VanillaData FromExecutable(string path) => FromImage(ImageOf(File.ReadAllBytes(path)));

    /// <summary>A PE file's sections placed at their virtual addresses, as the loader maps them.</summary>
    public static byte[] ImageOf(byte[] file)
    {
        var pe = BitConverter.ToInt32(file, 0x3C);
        if (pe <= 0 || pe + 24 > file.Length || BitConverter.ToUInt32(file, pe) != 0x00004550)
            throw new FormatException("Not a Windows executable.");
        var sectionCount = BitConverter.ToUInt16(file, pe + 6);
        var optionalSize = BitConverter.ToUInt16(file, pe + 20);
        var optional = pe + 24;
        var image = new byte[BitConverter.ToUInt32(file, optional + 56)];
        var headers = (int)Math.Min(BitConverter.ToUInt32(file, optional + 60), (uint)file.Length);
        Array.Copy(file, image, Math.Min(headers, image.Length));
        for (var i = 0; i < sectionCount; i++)
        {
            var section = optional + optionalSize + i * 40;
            var virtualAddress = BitConverter.ToInt32(file, section + 12);
            var rawSize = BitConverter.ToInt32(file, section + 16);
            var rawOffset = BitConverter.ToInt32(file, section + 20);
            var length = Math.Min(Math.Min(rawSize, file.Length - rawOffset), image.Length - virtualAddress);
            if (length > 0 && rawOffset > 0)
                Array.Copy(file, rawOffset, image, virtualAddress, length);
        }
        return image;
    }

    /// <summary>Copies the tables out of the running game (read-only).</summary>
    [SupportedOSPlatform("windows")]
    public static VanillaData FromProcess(Process game)
    {
        var module = game.MainModule ?? throw new InvalidOperationException("The game's executable isn't visible.");
        var start = (long)module.BaseAddress;
        var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ, false, game.Id);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Couldn't open the game to read it.");
        try
        {
            var memory = new ProcessMemory(handle);
            return FromReader(memory.Read(start, 0x1000), (offset, length) => memory.Read(start + offset, length));
        }
        finally
        {
            ProcessMemory.Native.CloseHandle(handle);
        }
    }

    private static VanillaData FromReader(ReadOnlySpan<byte> header, Func<long, int, byte[]> read)
    {
        var build = GameBuilds.Identify(header)
            ?? throw new NotSupportedException("This version of the game isn't supported yet.");
        var jobs = read(build.Tables[Jobs.Schema.Name], Jobs.Schema.Length);
        if (JobTableFingerprint.Find(jobs) is not [0])
            throw new InvalidOperationException("The game's tables aren't loaded yet. Wait for the title screen and try again.");
        return new VanillaData
        {
            BuildName = build.Name,
            TimeDateStamp = build.TimeDateStamp,
            SizeOfImage = build.SizeOfImage,
            Tables = build.Tables.ToDictionary(t => t.Key, t => read(t.Value, AllTables.ByName[t.Key].Length)),
        };
    }
}
