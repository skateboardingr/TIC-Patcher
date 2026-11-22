using System.Runtime.Versioning;
using TICPatcher.Core.Archives;
using TICPatcher.Core.Text;

namespace TICPatcher.Core.Maps;

/// <summary>
/// The battle maps of the installed game (read-only): which there are, their names
/// (TIC's own, from <see cref="GameText"/>), and their tiles, read from
/// data\enhanced\0002.pac when first needed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MapSource(string gameExe, GameText? text) : IDisposable
{
    private readonly PacArchive _archive = PacArchive.Open(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameExe))!, "data", "enhanced", "0002.pac"));
    private readonly Lazy<GDeflate> _codec = new(() => new GDeflate(Path.GetDirectoryName(Path.GetFullPath(gameExe))!));
    private readonly Dictionary<int, MapTerrain?> _cache = [];

    /// <summary>The maps in the archive (a few are cutscene-only and have no tiles: <see cref="Load"/> gives null).</summary>
    public List<int> Maps => _maps ??= [.. MapTerrain.MapsIn(_archive)];
    private List<int>? _maps;

    public string? MapName(int map) => text?.MapName(map);

    public string? TerrainName(int surface) => text?.Get(TextTables.Terrain, surface, "Name") is { Length: > 0 } name ? name : null;

    public int? Guess(string? battleName) => text is null ? null : BattleMaps.Guess(battleName, text, Maps);

    /// <summary>A map's tiles, or null if it has none.</summary>
    public MapTerrain? Load(int map)
    {
        if (_cache.TryGetValue(map, out var known))
            return known;
        MapTerrain? terrain;
        try
        {
            terrain = MapTerrain.Read(_archive, _codec.Value, map);
        }
        catch (Exception error) when (error is FormatException or FileNotFoundException or InvalidDataException)
        {
            terrain = null;
        }
        return _cache[map] = terrain;
    }

    private readonly Dictionary<int, byte[]?> _sheets = [];

    /// <summary>A unit sprite sheet (see <see cref="Sprites.UnitSprite"/>), or null if there's none.</summary>
    public byte[]? Sheet(int sprite)
    {
        if (_sheets.TryGetValue(sprite, out var known))
            return known;
        byte[]? sheet = null;
        if (Sprites.UnitSprite.FileOf(sprite) is { } file && _archive.Find(file) is { } entry)
        {
            try
            {
                sheet = _archive.Read(entry, _codec.Value);
            }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                sheet = null;
            }
        }
        return _sheets[sprite] = sheet;
    }

    public void Dispose()
    {
        if (_codec.IsValueCreated)
            _codec.Value.Dispose();
    }
}
