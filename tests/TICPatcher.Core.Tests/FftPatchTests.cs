using System.IO.Compression;
using System.Text;
using TICPatcher.Core.Import;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Tests;

// Generated data only: a made-up "TIC", FFTPatcher defaults and patch.
public sealed class FftPatchTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"tic-{Guid.NewGuid():N}.fftpatch");

    public void Dispose() => File.Delete(_file);

    private static void Set(byte[] table, int id, string field, string value) =>
        RecordCodec.Write(Jobs.Schema.Field(field), table.AsSpan(id * Jobs.Schema.RecordSize, Jobs.Schema.RecordSize), value);

    private static byte[] JobsWith(Action<byte[]> change)
    {
        var bytes = new byte[Jobs.Schema.Length];
        change(bytes);
        return bytes;
    }

    private void WritePatch(string type, Dictionary<string, byte[]> elements)
    {
        using var zip = ZipFile.Open(_file, ZipArchiveMode.Create);
        foreach (var (name, bytes) in elements.Append(new("type", Encoding.UTF8.GetBytes(type))).Append(new("version", Encoding.UTF8.GetBytes("1"))))
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    [Fact]
    public void ImportsOnlyWhatThePatchChanged()
    {
        // TIC: job 1 moves 3, wears hats (not helmets); job 2 has HP constant 10.
        var tic = AllTables.List.ToDictionary(t => t.Name, t => new byte[t.Length]);
        tic[Jobs.Schema.Name] = JobsWith(j =>
        {
            Set(j, 1, "Move", "3");
            Set(j, 1, "Equipment", "Sword, Shield, Hat");
            Set(j, 2, "HPConstant", "10");
        });
        var project = new ModProject(new VanillaData { BuildName = "test", TimeDateStamp = 0, SizeOfImage = 0, Tables = tic });

        // War of the Lions as FFTPatcher has it: move 4, helmets, HP constant 12.
        var wotl = JobsWith(j =>
        {
            Set(j, 1, "Move", "4");
            Set(j, 1, "Equipment", "Sword, Shield, Helmet");
            Set(j, 2, "HPConstant", "12");
        });
        var defaults = new Dictionary<string, byte[]> { ["PSP/bin/Jobs.bin"] = wotl, ["PSP/bin/PoachProbabilities.bin"] = new byte[96] };

        // The patch: job 1 moves 6 and can wear robes; job 2 left alone.
        var patched = (byte[])wotl.Clone();
        Set(patched, 1, "Move", "6");
        Set(patched, 1, "Equipment", "Sword, Shield, Helmet, Robe");
        WritePatch("US_PSP", new() { ["jobs"] = patched, ["poaching"] = [1, 2, 3] });

        var report = FftPatch.Read(_file).ApplyTo(project, defaults);

        Assert.Equal("6", project.Current(Jobs.Schema, 1, "Move"));
        Assert.Equal("Sword, Shield, Hat, Robe", project.Current(Jobs.Schema, 1, "Equipment")); // TIC's hat kept, the robe added
        Assert.Equal("10", project.Current(Jobs.Schema, 2, "HPConstant"));                      // untouched by the patch, so TIC's
        Assert.Equal(2, report.Total);
        Assert.Contains(report.Skipped, s => s.StartsWith("poaching"));
    }

    [Fact]
    public void PsxJobsGainPspsFifthEquipmentByte()
    {
        var tic = AllTables.List.ToDictionary(t => t.Name, t => new byte[t.Length]);
        var project = new ModProject(new VanillaData { BuildName = "test", TimeDateStamp = 0, SizeOfImage = 0, Tables = tic });
        // PSX job records are 48 bytes: PSP's layout without byte 13.
        var psx = new byte[48 * 0xA0];
        var patched = (byte[])psx.Clone();
        patched[48 * 3 + 23] = 5; // PSX byte 23 is PSP byte 24: Move
        WritePatch("US_PSX", new() { ["jobs"] = patched });

        FftPatch.Read(_file).ApplyTo(project, new Dictionary<string, byte[]> { ["PSX-US/bin/Jobs.bin"] = psx });

        Assert.Equal("5", project.Current(Jobs.Schema, 3, "Move"));
    }

    [Fact]
    public void RejectsOtherVersions()
    {
        WritePatch("PAL_PSX", []);
        Assert.Throws<FormatException>(() => FftPatch.Read(_file));
    }
}
