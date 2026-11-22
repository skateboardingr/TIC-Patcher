using System.Text.RegularExpressions;
using TICPatcher.Core.Archives;

namespace TICPatcher.Core.Maps;

/// <summary>One tile of a battle map.</summary>
/// <param name="Surface">The terrain type, a row of land.nxd (e.g. 3 Grassland, 14 Canal, 21 Flagstone).</param>
/// <param name="Height">Height in half-steps above the map's floor.</param>
/// <param name="Depth">Water depth (0 for dry land).</param>
/// <param name="SlopeHeight">How much the tile rises across it.</param>
/// <param name="SlopeType">Which way it slopes (not yet decoded).</param>
public sealed record MapTile(int Surface, int Height, int Depth, int SlopeHeight, int SlopeType)
{
    /// <summary>No tile here (outside the map's shape, or an empty upper-level slot).</summary>
    public bool IsEmpty => Surface == 0 && Height == 0 && Depth == 0 && SlopeHeight == 0 && SlopeType == 0;
}

/// <summary>
/// A battle map's tiles: a grid of <see cref="Width"/> by <see cref="Length"/>, with a lower
/// level and an upper one (bridges, walkways). Worked out from TIC's map files
/// (data\enhanced\0002.pac, map/map_mapNNN_*.bin), which are the PSX game's:
/// <code>
/// map_mapNNN_gns.bin   20-byte entries: resource type at +5 (0x2E = the map's geometry),
///                      its number at +12; resource N is the map's N-th numbered file
/// geometry file        a table of section offsets; the u32 at 0x68 points at the tiles:
///                      u8 width, u8 length, then 256 tiles per level (lower, then upper),
///                      row by row, 8 bytes each:
///   +0 surface (low 6 bits), +2 height, +3 water depth (top 3 bits) and slope height
///   (low 5 bits), +4 slope type; the other bytes aren't decoded yet
/// </code>
/// Map 1 (Eagrose Castle Gate) reads as a flagstone bridge over a canal between grass
/// banks, with the castle wall and its gate along one edge and a walkway over the gate.
/// </summary>
public sealed class MapTerrain
{
    private const int TilesPerLevel = 256;

    public required int Width { get; init; }
    public required int Length { get; init; }
    public required MapTile[] Lower { get; init; }
    public required MapTile[] Upper { get; init; }

    public MapTile At(int x, int y, bool upper = false) => (upper ? Upper : Lower)[y * Width + x];

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Length;

    /// <summary>The tiles from a map's geometry file.</summary>
    public static MapTerrain Parse(byte[] geometry)
    {
        var at = (int)BitConverter.ToUInt32(geometry, 0x68);
        if (at <= 0 || at + 2 + 2 * TilesPerLevel * 8 > geometry.Length)
            throw new FormatException("This map file has no tile section.");
        int width = geometry[at], length = geometry[at + 1];
        if (width == 0 || length == 0 || width * length > TilesPerLevel)
            throw new FormatException($"Odd map size {width} x {length}.");
        MapTile[] Level(int level) => Enumerable.Range(0, width * length).Select(i =>
        {
            var t = geometry.AsSpan(at + 2 + (level * TilesPerLevel + i) * 8, 8);
            return new MapTile(t[0] & 0x3F, t[2], t[3] >> 5, t[3] & 0x1F, t[4]);
        }).ToArray();
        return new MapTerrain { Width = width, Length = length, Lower = Level(0), Upper = Level(1) };
    }

    private static readonly Regex MapFile = new(@"^map/map_map(\d{3})_(\d+)\.bin$", RegexOptions.IgnoreCase);

    /// <summary>The map numbers in an archive (0002.pac) that have tiles to read.</summary>
    public static IReadOnlyList<int> MapsIn(PacArchive archive) =>
        archive.Entries.Select(e => Regex.Match(e.Name, @"^map/map_map(\d{3})_gns\.bin$", RegexOptions.IgnoreCase))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Order().ToList();

    /// <summary>A map's tiles, read from the game's archive (0002.pac).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static MapTerrain Read(PacArchive archive, GDeflate codec, int map)
    {
        var gns = archive.Read(archive.Find($"map/map_map{map:000}_gns.bin") ?? throw new FileNotFoundException($"Map {map} isn't in {archive.Name}."), codec);
        // The map's numbered files, in order: resource N is the N-th.
        var files = archive.Entries
            .Select(e => (Entry: e, Match: MapFile.Match(e.Name)))
            .Where(f => f.Match.Success && int.Parse(f.Match.Groups[1].Value) == map)
            .OrderBy(f => int.Parse(f.Match.Groups[2].Value))
            .Select(f => f.Entry)
            .ToList();
        for (var i = 0; i + 20 <= gns.Length; i += 20)
        {
            if (gns[i + 5] != 0x2E)
                continue;
            var resource = (int)BitConverter.ToUInt32(gns, i + 12);
            if (resource < files.Count)
                return Parse(archive.Read(files[resource], codec));
        }
        throw new FormatException($"Map {map} has no geometry.");
    }
}
