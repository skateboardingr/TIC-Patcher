namespace TICPatcher.Core.Sprites;

/// <summary>
/// How a battle sprite's poses are put together from its sheet: a shape file
/// (unit/battle_NAME_shp.bin in data\enhanced\0002.pac) lists frames, each a list of
/// rectangles copied from the sheet to a spot in the frame, maybe mirrored. Humans'
/// sheets hold whole poses, but chocobos and many monsters are kits of parts, so a
/// picture of them has to be assembled this way. Frame 2 is a unit standing (frame 0 is
/// often blank).
/// <code>
/// shape file  u32 second list, u16 first frame using the sheet's lower half; frame offsets
///             (u32) from 0x0C, ending at 0; frame N at its offset + 0x40A (the first
///             frame's offset is 0)
/// frame       u16 tile count - 1, then 4 bytes a tile, drawn last to first:
///             s8 x, s8 y, u16: bits 0-4 sheet column / 8, 5-9 row / 8, 10-13 size,
///             bit 14 mirrored left-right, bit 15 upside down
/// </code>
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by Joe Davidson and
/// Glain, ShishiSpriteEditor/DataTypes/Shape.cs, Frame.cs, Tile.cs; shape types from
/// DataTypes/Sprites/MemoryLocs.txt.
/// </summary>
public static class SpriteShape
{
    /// <summary>Each sprite id's (0x01 first) shape type: 0 TYPE1, 1 TYPE2, 2 CYOKO, 3 MON, 6 ARUTE, 7 KANZEN.</summary>
    private static readonly byte[] Types =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1, 1, 0, 1, 1, 1, 0, 0, 0,
        1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 1, 0, 1, 1, 0, 0, 0, 1, 1, 1, 1, 0, 1, 1, 3, 0, 3, 0, 3,
        6, 1, 3, 1, 3, 1, 1, 3, 7, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 3, 0, 1, 0,
        0, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 1,
        1, 1, 1, 1, 1, 2, 0, 3, 3, 1, 0, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    ];

    /// <summary>The archive file of a sprite's shape, or null if it's unknown.</summary>
    public static string? FileOf(int sprite)
    {
        if (sprite < 1 || sprite > Types.Length)
            return null;
        return Types[sprite - 1] switch
        {
            0 => "unit/battle_type1_shp.bin",
            1 => "unit/battle_type2_shp.bin",
            2 => "unit/battle_cyoko_shp.bin",
            3 => "unit/battle_mon_shp.bin",
            6 => "unit/battle_arute_shp.bin",
            7 => "unit/battle_kanzen_shp.bin",
            _ => null,
        };
    }

    /// <summary>The frame shown standing.</summary>
    public const int Standing = 2;

    private static readonly (int W, int H)[] Sizes =
    [
        (8, 8), (16, 8), (16, 16), (16, 24), (24, 8), (24, 16), (24, 24), (32, 8),
        (32, 16), (32, 24), (32, 32), (32, 40), (48, 16), (40, 32), (48, 48), (56, 56),
    ];

    /// <summary>One frame put together from a sheet's pixels (<see cref="UnitSprite.Pixels"/>): palette indices, cropped to what's drawn.</summary>
    public sealed record Picture(int Width, int Height, byte[] Pixels);

    /// <summary>Frame <paramref name="index"/> of a shape, assembled from a sheet's pixels; null if the shape has no such frame or it's blank.</summary>
    public static Picture? Frame(byte[] shape, byte[] sheetPixels, int index)
    {
        var second = shape[4] | shape[5] << 8;
        var offsets = new List<int> { 0 };
        for (var i = 0; 0x0C + 4 * i + 4 <= shape.Length; i++)
        {
            var offset = (int)BitConverter.ToUInt32(shape, 0x0C + 4 * i);
            if (offset == 0)
                break;
            offsets.Add(offset);
        }
        if (index >= offsets.Count)
            return null;
        var at = offsets[index] + 0x40A;
        if (at + 2 > shape.Length)
            return null;
        var yOffset = index >= second ? 256 : 0;
        // Frames are drawn on a 185 x 250 canvas (FFTPatcher's), tiles placed at (x + 53, y + 118).
        const int CanvasW = 185, CanvasH = 250;
        var canvas = new byte[CanvasW * CanvasH];
        var tiles = (shape[at] | shape[at + 1] << 8) + 1;
        for (var t = tiles - 1; t >= 0; t--)
        {
            var o = at + 2 + t * 4;
            if (o + 4 > shape.Length)
                continue;
            int x = (sbyte)shape[o] + 53, y = (sbyte)shape[o + 1] + 118;
            var flags = shape[o + 2] | shape[o + 3] << 8;
            var (w, h) = Sizes[(flags >> 10) & 0x0F];
            int sx = (flags & 0x1F) * 8, sy = ((flags >> 5) & 0x1F) * 8 + yOffset;
            bool flipX = (flags & 0x4000) != 0, flipY = (flags & 0x8000) != 0;
            for (var dy = 0; dy < h; dy++)
                for (var dx = 0; dx < w; dx++)
                {
                    int px = sx + (flipX ? w - 1 - dx : dx), py = sy + (flipY ? h - 1 - dy : dy);
                    int cx = x + dx, cy = y + dy;
                    if (px >= 256 || py * 256 + px >= sheetPixels.Length || cx < 0 || cy < 0 || cx >= CanvasW || cy >= CanvasH)
                        continue;
                    var value = sheetPixels[py * 256 + px];
                    if (value != 0)
                        canvas[cy * CanvasW + cx] = value;
                }
        }
        // Crop to what was drawn.
        int left = CanvasW, top = CanvasH, right = -1, bottom = -1;
        for (var cy = 0; cy < CanvasH; cy++)
            for (var cx = 0; cx < CanvasW; cx++)
                if (canvas[cy * CanvasW + cx] != 0)
                {
                    left = Math.Min(left, cx);
                    right = Math.Max(right, cx);
                    top = Math.Min(top, cy);
                    bottom = Math.Max(bottom, cy);
                }
        if (right < 0)
            return null;
        int width = right - left + 1, height = bottom - top + 1;
        var pixels = new byte[width * height];
        for (var cy = 0; cy < height; cy++)
            Array.Copy(canvas, (top + cy) * CanvasW + left, pixels, cy * width, width);
        return new Picture(width, height, pixels);
    }
}
