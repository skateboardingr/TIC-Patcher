using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TICPatcher.Core.Maps;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Sprites;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.App;

/// <summary>
/// Pictures of unit sprites for the editor (the battle map, previews, the Sprites tab),
/// in the mod's current palette colours, so a recoloured palette shows everywhere at once.
/// </summary>
public sealed class SpritePictures(ModProject project, MapSource maps)
{
    private readonly Dictionary<string, Bitmap?> _cache = [];

    public MapSource Maps => maps;

    /// <summary>A palette's 16 colours as the mod has them (or the sheet's own, without the palette table).</summary>
    public ushort[] Colours(int sprite, int palette)
    {
        var record = sprite * 16 + Math.Clamp(palette, 0, 15);
        if (project.HasTable(SpriteSheets.Palettes.Name) && sprite >= 1 && sprite <= SpriteSheets.Count)
            return Enumerable.Range(0, 16).Select(i => ushort.Parse(project.Current(SpriteSheets.Palettes, record, $"Color{i}"))).ToArray();
        return maps.Sheet(sprite) is { } sheet ? UnitSprite.PaletteOf(sheet, palette) : new ushort[16];
    }

    /// <summary>
    /// A sprite standing, in a palette: assembled from its shape (so chocobos and monsters built
    /// from parts come out whole), or the sheet's first frame if it has no known shape.
    /// </summary>
    public Bitmap? Frame(int sprite, int palette)
    {
        var colours = Colours(sprite, palette);
        var key = $"{sprite}:standing:{string.Join(",", colours)}";
        if (_cache.TryGetValue(key, out var known))
            return known;
        if (SpriteShape.FileOf(sprite) is { } file && maps.File(file) is { } shape && PixelsOf(sprite) is { } pixels
            && SpriteShape.Frame(shape, pixels, SpriteShape.Standing) is { } picture)
            return _cache[key] = ToBitmap(UnitSprite.Render(picture.Pixels, picture.Width, colours, 0, 0, picture.Width, picture.Height), picture.Width, picture.Height);
        return _cache[key] = Picture(sprite, palette, 0, 0, UnitSprite.FrameWidth, UnitSprite.FrameHeight);
    }

    private byte[]? PixelsOf(int sprite)
    {
        if (!_pixels.TryGetValue(sprite, out var pixels))
            _pixels[sprite] = pixels = maps.Sheet(sprite) is { } sheet ? UnitSprite.Pixels(sheet) : null;
        return pixels;
    }

    private readonly Dictionary<int, byte[]?> _pixels = [];

    /// <summary>A sprite's whole sheet (256 x 488: poses, more poses, portrait) in a palette.</summary>
    public Bitmap? Sheet(int sprite, int palette)
    {
        var colours = Colours(sprite, palette);
        var key = $"{sprite}:sheet:{string.Join(",", colours)}";
        if (_cache.TryGetValue(key, out var known))
            return known;
        var pixels = PixelsOf(sprite);
        return _cache[key] = pixels is null ? null
            : ToBitmap(UnitSprite.Render(pixels, 256, colours, 0, 0, 256, UnitSprite.SheetRows), 256, UnitSprite.SheetRows);
    }

    private Bitmap? Picture(int sprite, int palette, int left, int top, int width, int height)
    {
        var colours = Colours(sprite, palette);
        var key = $"{sprite}:{left},{top},{width},{height}:{string.Join(",", colours)}";
        if (_cache.TryGetValue(key, out var known))
            return known;
        Bitmap? picture = null;
        if (maps.Sheet(sprite) is { } sheet)
            picture = ToBitmap(UnitSprite.Render(sheet, colours, left, top, width, height), width, height);
        return _cache[key] = picture;
    }

    /// <summary>A battle unit's sprite and palette, as the mod has it (null for an empty slot).</summary>
    public (int Sprite, int Palette)? OfUnit(int id)
    {
        string Current(string field) => project.Current(Battles.Units, id, field);
        var job = int.Parse(Current("Job"));
        var jobKnown = project.HasTable(Jobs.Schema.Name) && job < Jobs.Schema.RecordCount;
        return UnitSprite.Of(int.Parse(Current("SpriteSet")), Current("Flags1").Contains("Female"), job, int.Parse(Current("Palette")),
            jobKnown ? int.Parse(project.Current(Jobs.Schema, job, "MPortrait")) : 0,
            jobKnown ? int.Parse(project.Current(Jobs.Schema, job, "MPalette")) : 0);
    }

    public static Bitmap ToBitmap(byte[] rgba, int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        for (var y = 0; y < height; y++)
            System.Runtime.InteropServices.Marshal.Copy(rgba, y * width * 4, buffer.Address + y * buffer.RowBytes, width * 4);
        return bitmap;
    }

    /// <summary>A picture shown enlarged with sharp pixels, as pixel art should be.</summary>
    public static Image Show(IImage? picture, double scale)
    {
        var image = new Image
        {
            Source = picture,
            Width = (picture?.Size.Width ?? 0) * scale,
            Height = (picture?.Size.Height ?? 0) * scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        return image;
    }

    /// <summary>"Goblin (sprite 87), palette 1".</summary>
    public static string Caption(int sprite, int palette) =>
        $"{Core.Tables.Names.Get(Core.Tables.Names.SpriteFiles, sprite) ?? "Sprite"} (sprite {sprite:X2}), palette {palette}";
}
