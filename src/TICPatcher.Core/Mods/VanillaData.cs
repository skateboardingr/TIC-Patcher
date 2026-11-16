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
    /// Copies the tables out of an image of the game's module (its memory
    /// from the module start, as TICScan --dump writes it).
    /// </summary>
    public static VanillaData FromImage(byte[] image) =>
        FromReader(image.AsSpan(0, Math.Min(image.Length, 0x1000)), (offset, length) => image.AsSpan((int)offset, length).ToArray());

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
