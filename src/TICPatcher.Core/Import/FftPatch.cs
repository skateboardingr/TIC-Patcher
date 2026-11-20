using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Text;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Import;

/// <summary>
/// Imports an FFTPatcher patch (.fftpatch) made for the PSP or PSX game.
/// A patch holds every table in full, so what its author changed is found by
/// comparing it with FFTPatcher's own defaults (read from the user's copy of
/// FFTPatcher; TIC Patcher ships no game data). Only those changes are applied,
/// on top of TIC's own values, which the remaster rebalanced: a field the patch
/// left alone keeps TIC's value, and in flag fields only the bits the patch
/// changed are carried over.
///
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by Joe
/// Davidson and Glain, v0.498: Datatypes/FFTPatch.cs (file format, element
/// names), AllAbilities.cs, AllItems.cs, ItemAttributes.cs, Job.cs, JobLevels.cs,
/// SkillSet.cs, MonsterSkills.cs, ActionMenu.cs, Status/*.cs, StoreInventory.cs,
/// AllPropositions.cs (where each table sits in its element),
/// PatcherLib.Resources/Resources.cs (where the defaults are).
/// </summary>
public sealed class FftPatch
{
    public required bool Psp { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyDictionary<string, byte[]> Elements { get; init; }

    /// <summary>Reads a .fftpatch (a zip of the patch's tables).</summary>
    public static FftPatch Read(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var elements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                elements[entry.FullName] = memory.ToArray();
            }
            var type = elements.TryGetValue("type", out var t) ? Encoding.UTF8.GetString(t).Trim() : "";
            if (type is not ("US_PSP" or "US_PSX"))
                throw new FormatException($"This patch is for \"{type}\"; only US PSP and US PSX patches can be imported.");
            return new FftPatch
            {
                Psp = type == "US_PSP",
                Version = elements.TryGetValue("version", out var v) ? Encoding.UTF8.GetString(v).Trim() : "",
                Elements = elements,
            };
        }
        catch (InvalidDataException)
        {
            // Very old FFTPatcher versions saved XML instead.
            throw new FormatException("This is an old-style FFTPatcher patch. Open it in FFTPatcher and save it again, then import that.");
        }
    }

    /// <summary>
    /// FFTPatcher's default tables, from its PatcherLib.Resources.dll (given the
    /// dll or the folder FFTPatcher is in): file name (e.g. "PSP/bin/Jobs.bin") → bytes.
    /// </summary>
    public static Dictionary<string, byte[]> ReadDefaults(string fftPatcher)
    {
        var dll = Directory.Exists(fftPatcher) ? Path.Combine(fftPatcher, "PatcherLib.Resources.dll") : fftPatcher;
        if (!File.Exists(dll))
            throw new FileNotFoundException("PatcherLib.Resources.dll wasn't found; pick the folder FFTPatcher.exe is in.", dll);
        using var pe = new PEReader(File.OpenRead(dll));
        var metadata = pe.GetMetadataReader();
        byte[]? resources = null;
        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            if (!resource.Implementation.IsNil || !metadata.GetString(resource.Name).EndsWith("Properties.Resources.resources", StringComparison.Ordinal))
                continue;
            var directory = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
            var reader = directory.GetReader((int)resource.Offset, directory.Length - (int)resource.Offset);
            resources = reader.ReadBytes(reader.ReadInt32());
        }
        if (resources is null)
            throw new FormatException($"{dll} doesn't hold FFTPatcher's defaults.");

        using var resourceReader = new ResourceReader(new MemoryStream(resources));
        resourceReader.GetResourceData("ZippedResources", out _, out var data);
        // A byte array resource: its length, then the bytes (a .tar.gz).
        var tarGz = data.AsSpan(4, BitConverter.ToInt32(data, 0)).ToArray();
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var tar = new TarReader(new GZipStream(new MemoryStream(tarGz), CompressionMode.Decompress));
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.DataStream is null || entry.Length == 0)
                continue;
            using var memory = new MemoryStream();
            entry.DataStream.CopyTo(memory);
            files[entry.Name.Replace('\\', '/').TrimStart('.', '/')] = memory.ToArray();
        }
        return files;
    }

    /// <summary>Where a TIC table's records sit in a patch element.</summary>
    /// <param name="Psx">How the PSX game differs: its record size and count, or null if the PSP layout is used for both.</param>
    private sealed record Part(string Element, string DefaultFile, TableSchema Table, int Offset, int RecordSize, int Count,
        (int RecordSize, int Count)? Psx = null, bool PspOnly = false);

    private static readonly Part[] Parts =
    [
        new("abilities", "Abilities.bin", Abilities.Common, 0, 8, 512),
        new("abilities", "Abilities.bin", Abilities.Action, 0x1000, 14, 0x170),
        new("abilities", "Abilities.bin", Abilities.Throw, 0x2430, 1, 12),
        new("abilities", "Abilities.bin", Abilities.Jump, 0x243C, 2, 12),
        new("abilities", "Abilities.bin", Abilities.Charge, 0x2454, 2, 8),
        new("abilityEffects", "AbilityEffects.bin", Abilities.Effects, 0, 2, 0x1C6, PspOnly: true),
        new("abilityAnimations", "AbilityAnimations.bin", Abilities.Animations, 0, 3, 0x1C6),
        new("items", "OldItems.bin", Items.Common, 0, 12, 0x100),
        new("items", "OldItems.bin", Items.Weapons, 0xC00, 8, 0x80),
        new("items", "OldItems.bin", Items.Shields, 0x1000, 2, 0x10),
        new("items", "OldItems.bin", Items.Armor, 0x1020, 2, 0x40),
        new("items", "OldItems.bin", Items.Accessories, 0x10A0, 2, 0x20),
        new("items", "OldItems.bin", Items.ChemistItems, 0x10E0, 3, 0x0E),
        new("itemAttributes", "OldItemAttributes.bin", Items.Attributes, 0, 25, 0x50),
        new("jobs", "Jobs.bin", Jobs.Schema, 0, 49, 0xA9, Psx: (48, 0xA0)),
        new("jobLevels", "JobLevels.bin", SkillSets.JobRequirements, 0, 12, 22, Psx: (10, 19)),
        new("skillSets", "SkillSetsBin.bin", SkillSets.Main, 0, 25, 0xB0),
        new("skillSets", "SkillSetsBin.bin", SkillSets.Extra, 0xB0 * 25, 25, 3, PspOnly: true),
        new("monsterSkills", "MonsterSkills.bin", SkillSets.MonsterSkills, 0, 5, 48),
        new("actionMenus", "ActionEvents.bin", SkillSets.ActionMenus, 0, 1, 0xE3, Psx: (1, 0xE0)),
        new("statusAttributes", "StatusAttributes.bin", Statuses.Attributes, 0, 16, 40),
        new("inflictStatuses", "InflictStatuses.bin", Statuses.Inflict, 0, 6, 0x80),
        new("storeInventories", "StoreInventories.bin", Items.ShopInventories, 0, 2, 256),
        new("propositions", "Propositions.bin", Errands.Schema, 0, 23, 96),
    ];

    // Shared by both games in FFTPatcher's defaults; the rest are under PSP/bin or PSX-US/bin.
    private static readonly HashSet<string> SharedDefaults = ["StoreInventories.bin", "Propositions.bin"];

    /// <summary>Patch contents TIC Patcher can't import (yet): element → (FFTPatcher's default file, why).
    /// "{bin}" is PSP/bin/ or PSX-US/bin/.</summary>
    private static readonly Dictionary<string, (string Default, string Why)> NotImported = new(StringComparer.Ordinal)
    {
        ["pspItems"] = ("PSP/bin/NewItems.bin", "War of the Lions' extra items (TIC's items after 0xFF are different ones)"),
        ["pspItemAttributes"] = ("PSP/bin/NewItemAttributes.bin", "War of the Lions' extra item attributes"),
        ["poaching"] = ("{bin}PoachProbabilities.bin", "poaching (not in TIC's executable)"),
        ["moveFindItems"] = ("MoveFind.bin", "move-find items (not in TIC's executable)"),
        ["entd1"] = ("ENTD1.ENT", "battles (ENTD)"),
        ["entd2"] = ("ENTD2.ENT", "battles (ENTD)"),
        ["entd3"] = ("ENTD3.ENT", "battles (ENTD)"),
        ["entd4"] = ("ENTD4.ENT", "battles (ENTD)"),
        ["entd5"] = ("PSP/bin/ENTD5.bin", "battles (ENTD)"),
        ["jobFormationSprites1"] = ("JobFormationSprites1.bin", "job formation sprites"),
        ["jobFormationSprites2"] = ("JobFormationSprites2.bin", "job formation sprites"),
    };

    /// <summary>
    /// Applies the patch's changes to the project. Returns a summary of what was
    /// imported and what couldn't be.
    /// </summary>
    public ImportReport ApplyTo(ModProject project, IReadOnlyDictionary<string, byte[]> defaults)
    {
        var report = new ImportReport();
        var folder = Psp ? "PSP/bin/" : "PSX-US/bin/";
        foreach (var part in Parts)
        {
            if (!project.HasTable(part.Table.Name) || (part.PspOnly && !Psp))
                continue;
            var (size, count) = Psp ? (part.RecordSize, part.Count) : part.Psx ?? (part.RecordSize, part.Count);
            var file = SharedDefaults.Contains(part.DefaultFile) ? part.DefaultFile : folder + part.DefaultFile;
            if (!Elements.TryGetValue(part.Element, out var patch) || !defaults.TryGetValue(file, out var original))
                continue;
            if (part.Element == "abilityEffects" && original.Length != 0x38C)
                continue; // FFTPatcher rebuilds these defaults from several files; not worth guessing
            for (var i = 0; i < count; i++)
            {
                var at = part.Offset + i * size;
                if (at + size > patch.Length || at + size > original.Length)
                    break;
                var mine = patch.AsSpan(at, size);
                var theirs = original.AsSpan(at, size);
                if (mine.SequenceEqual(theirs))
                    continue;
                // PSX jobs gain PSP's fifth equipment byte when widened.
                var length = !Psp && part.Table == Jobs.Schema ? size + 1 : size;
                ImportRecord(project, part.Table, part.Table.FirstId + i, Widen(part, mine), Widen(part, theirs), length, report);
            }
        }
        // Changed (or with no default to compare with): worth telling the user it wasn't imported.
        foreach (var (element, (file, why)) in NotImported)
            if (Elements.TryGetValue(element, out var bytes)
                && !(defaults.TryGetValue(file.Replace("{bin}", folder), out var original) && original.AsSpan().SequenceEqual(bytes)))
                report.Skipped.Add(why);
        return report;
    }

    /// <summary>
    /// Research: how many of FFTPatcher's PSP default records are byte-for-byte
    /// TIC's (over the PSP record's length), per table. A mapping that's off shows as near zero.
    /// </summary>
    public static IEnumerable<(string Table, int Same, int Total)> CompareWithTic(IReadOnlyDictionary<string, byte[]> defaults, VanillaData vanilla)
    {
        foreach (var part in Parts)
        {
            var file = SharedDefaults.Contains(part.DefaultFile) ? part.DefaultFile : "PSP/bin/" + part.DefaultFile;
            if (!defaults.TryGetValue(file, out var original) || !vanilla.Tables.TryGetValue(part.Table.Name, out var tic))
                continue;
            var same = 0;
            var total = 0;
            for (var i = 0; i < part.Count && part.Offset + (i + 1) * part.RecordSize <= original.Length; i++)
            {
                total++;
                if (original.AsSpan(part.Offset + i * part.RecordSize, part.RecordSize)
                    .SequenceEqual(tic.AsSpan(i * part.Table.RecordSize, part.RecordSize)))
                    same++;
            }
            yield return (part.Table.Name, same, total);
        }
    }

    // A record in TIC's layout: PSX jobs lack PSP's fifth equipment byte (offset 13);
    // anything TIC has after the PSP record (e.g. action abilities' bytes 14-19) is left zero.
    private byte[] Widen(Part part, ReadOnlySpan<byte> record)
    {
        var bytes = new byte[Math.Max(part.Table.RecordSize, record.Length + 1)];
        if (!Psp && part.Table == Jobs.Schema)
        {
            record[..13].CopyTo(bytes);
            record[13..].CopyTo(bytes.AsSpan(14));
        }
        else
            record.CopyTo(bytes);
        return bytes;
    }

    private void ImportRecord(ModProject project, TableSchema table, int id, byte[] mine, byte[] theirs, int size, ImportReport report)
    {
        var covered = new bool[size + 1];
        foreach (var field in table.Fields)
        {
            var end = field.Kind == FieldKind.AbilityId ? Math.Max(field.Offset, field.HighOffset) : field.Offset + field.Size - 1;
            if (end >= size)
                continue; // not in the patch's record
            for (var b = field.Offset; b < field.Offset + field.Size; b++)
                covered[b] = true;
            if (field.Kind == FieldKind.AbilityId)
                covered[field.HighOffset] = true;
            var wanted = RecordCodec.Read(field, mine);
            var before = RecordCodec.Read(field, theirs);
            if (field.Kind == FieldKind.Flags)
            {
                // Only the flags the patch changed; TIC's own value for the rest.
                var now = RecordCodec.ReadFlags(field, mine).ToHashSet();
                var was = RecordCodec.ReadFlags(field, theirs).ToHashSet();
                var changed = now.Union(was).Except(now.Intersect(was)).ToList();
                if (changed.Count == 0)
                    continue;
                var current = project.Current(table, id, field.Name).Split(", ", StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                foreach (var flag in changed)
                {
                    if (now.Contains(flag))
                        current.Add(flag);
                    else
                        current.Remove(flag);
                }
                // Keep the field's own order.
                wanted = string.Join(", ", (field.FlagNames ?? []).OfType<string>().Where(current.Contains));
            }
            else if (wanted == before)
                continue;
            if (project.Current(table, id, field.Name) == wanted)
                continue;
            project.Set(table, id, field.Name, wanted);
            report.Changes[table.Name] = report.Changes.GetValueOrDefault(table.Name) + 1;
        }
        // Changed bytes no field covers (bits TIC Patcher doesn't name yet).
        for (var b = 0; b < size; b++)
            if (!covered[b] && mine[b] != theirs[b])
                report.Unmapped[table.Name] = report.Unmapped.GetValueOrDefault(table.Name) + 1;
    }
}

/// <summary>What an import did.</summary>
public sealed class ImportReport
{
    /// <summary>Table → fields changed.</summary>
    public Dictionary<string, int> Changes { get; } = new(StringComparer.Ordinal);

    /// <summary>Table → changed bytes that no field covers.</summary>
    public Dictionary<string, int> Unmapped { get; } = new(StringComparer.Ordinal);

    /// <summary>Changed parts of the patch TIC Patcher can't import.</summary>
    public SortedSet<string> Skipped { get; } = new(StringComparer.Ordinal);

    public int Total => Changes.Values.Sum();

    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append(Total == 0 ? "Nothing in the patch differs from FFTPatcher's defaults in tables TIC has." : $"Imported {Total} change(s): ");
        text.Append(string.Join(", ", Changes.OrderBy(c => c.Key).Select(c => $"{c.Key} {c.Value}")));
        if (Unmapped.Count > 0)
            text.Append($". Not imported: changed bytes TIC Patcher doesn't name yet ({string.Join(", ", Unmapped.Select(u => $"{u.Key} {u.Value}"))})");
        if (Skipped.Count > 0)
            text.Append($". Not in TIC Patcher yet: {string.Join("; ", Skipped)}");
        return text.Append('.').ToString();
    }
}
