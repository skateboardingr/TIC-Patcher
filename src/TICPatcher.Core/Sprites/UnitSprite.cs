namespace TICPatcher.Core.Sprites;

/// <summary>
/// Battle unit sprites: which sprite sheet a unit uses, and its pictures. The sheets
/// are the PSX game's, in data\enhanced\0002.pac as unit/battle_NAME_spr.bin:
/// <code>
/// 0x000  16 palettes of 16 colours, 2 bytes each (5 bits red, green, blue; colour 0 is see-through)
/// 0x200  the sheet, 256 pixels wide, 4 bits a pixel (low nibble first); the first 256 rows
///        are stored as they are, the rest (more poses) compressed (not read yet)
/// </code>
/// The first frame (standing, facing the viewer's lower left) is the top-left 32 x 40 pixels.
///
/// Which sheet: a battle unit's sprite set picks it directly for story characters (below 0x80);
/// generic humans (0x80 male, 0x81 female) use their job's, in male/female pairs; monsters (0x82)
/// use their job's MPortrait (sprite) and MPalette (palette) fields. Checked on the game's data:
/// Goblin MPortrait 0x87 = GOB, Black Goblin the same sheet in palette 1.
///
/// Source for the sprite order: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by Joe
/// Davidson and Glain, ShishiSpriteEditor/DataTypes/Sprites/MemoryLocs.txt.
/// </summary>
public static class UnitSprite
{
    /// <summary>Sprite id → sheet name (id 0x01 is the first).</summary>
    private static readonly string[] Sheets =
    [
        "ramuza", "ramuza2", "ramuza3", "dily", "dily2", "dily3", "aru", "zaru", "daisu", "ragu", "goru", "hime", "oru", "fyune", "reze", "zarumou",
        "baruna", "gando", "simon", "gyumu", "oran", "garu", "h61", "dora", "rafa", "mara", "eru", "furaia", "bariten", "kanba", "beio", "wigu",
        "baru", "musu", "rudo", "voru", "h75", "h76", "h77", "h78", "h79", "h80", "h81", "h82", "h83", "arufu", "h85", "aruma",
        "ajora", "cloud", "zaru2", "aguri", "item_w", "siro_w", "kuro_m", "onmyo_m", "mina_m", "seria", "ledy", "veri", "knight_m", "zarue", "yumi_m", "hasyu",
        "arute", "kuro_m", "kyuku", "toki_w", "adora", "onmyo_m", "syou_w", "reze_d", "kanzen", "10m", "10w", "20m", "20w", "40m", "40w", "60m",
        "60w", "cyomon1", "cyomon2", "cyomon3", "cyomon4", "souryo", "mina_m", "mina_m", "mina_m", "mina_m", "mina_m", "mina_m", "dami", "kasanek", "kasanem", "mina_m",
        "mina_w", "item_m", "item_w", "knight_m", "knight_w", "yumi_m", "yumi_w", "monk_m", "monk_w", "siro_m", "siro_w", "kuro_m", "kuro_w", "toki_m", "toki_w", "syou_m",
        "syou_w", "thief_m", "thief_w", "waju_m", "waju_w", "onmyo_m", "onmyo_w", "fusui_m", "fusui_w", "ryu_m", "ryu_w", "samu_m", "samu_w", "ninja_m", "ninja_w", "san_m",
        "san_w", "gin_m", "odori_w", "mono_m", "mono_w", "cyoko", "gob", "bom", "hyou", "ika", "sukeru", "yurei", "arli", "tori", "uri", "ki",
        "minota", "mol", "behi", "dora1", "dora2", "biburos", "hebi", "dora1", "demon", "tetsu",
    ];

    /// <summary>The archive file of a sprite id's sheet, or null if it's unknown.</summary>
    public static string? FileOf(int sprite) =>
        sprite >= 1 && sprite <= Sheets.Length ? $"unit/battle_{Sheets[sprite - 1]}_spr.bin" : null;

    /// <summary>
    /// The sprite id and palette of a battle unit, from its sprite set, gender, job and palette
    /// field, and the job's MPortrait and MPalette (from the jobs table as the mod has it).
    /// </summary>
    public static (int Sprite, int Palette)? Of(int spriteSet, bool female, int job, int palette, int jobPortrait, int jobPalette)
    {
        switch (spriteSet)
        {
            case 0:
                return null;
            case < 0x80:
                return (spriteSet, palette);
            case 0x82:
                return jobPortrait > 0 ? (jobPortrait, jobPalette) : null;
            case 0x80 or 0x81:
                // Squire (0x4A) to Arithmetician (0x5A): male, female pairs from 0x60; then Bard (male only),
                // Dancer (female only) and Mime.
                int? sprite = job switch
                {
                    >= 0x4A and <= 0x5A => 0x60 + 2 * (job - 0x4A) + (female ? 1 : 0),
                    0x5B => 0x82,
                    0x5C => 0x83,
                    0x5D => 0x84 + (female ? 1 : 0),
                    _ => null,
                };
                return sprite is { } s ? (s, palette) : null;
            default:
                return null;
        }
    }

    /// <summary>A palette's 16 colours as stored in a sheet (or a mod's palette table).</summary>
    public static ushort[] PaletteOf(byte[] sheet, int palette)
    {
        palette = Math.Clamp(palette, 0, 15);
        return Enumerable.Range(0, 16).Select(i => sheet.Length >= palette * 32 + i * 2 + 2 ? BitConverter.ToUInt16(sheet, palette * 32 + i * 2) : (ushort)0).ToArray();
    }

    /// <summary>A colour as red, green and blue (0-255).</summary>
    public static (byte R, byte G, byte B) Rgb(ushort colour) =>
        ((byte)((colour & 31) * 255 / 31), (byte)(((colour >> 5) & 31) * 255 / 31), (byte)(((colour >> 10) & 31) * 255 / 31));

    /// <summary>
    /// Part of a sheet's stored (uncompressed) area in the given colours, as RGBA bytes,
    /// colour 0 see-through; rows beyond the stored area come out empty.
    /// </summary>
    public static byte[] Render(byte[] sheet, IReadOnlyList<ushort> colours, int left, int top, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = 0x200 + ((top + y) * 256 + left + x) / 2;
                if (at >= sheet.Length || top + y >= StoredRows)
                    continue;
                var index = (left + x) % 2 == 0 ? sheet[at] & 0x0F : sheet[at] >> 4;
                if (index == 0)
                    continue;
                var (r, g, b) = Rgb(colours[index]);
                var o = (y * width + x) * 4;
                rgba[o] = r;
                rgba[o + 1] = g;
                rgba[o + 2] = b;
                rgba[o + 3] = 255;
            }
        return rgba;
    }

    /// <summary>A sheet's first frame (standing) in some colours, as RGBA bytes.</summary>
    public static byte[] FirstFrame(byte[] sheet, IReadOnlyList<ushort> colours) => Render(sheet, colours, 0, 0, FrameWidth, FrameHeight);

    /// <summary>A sheet's first frame in one of its own palettes.</summary>
    public static byte[] FirstFrame(byte[] sheet, int palette) => FirstFrame(sheet, PaletteOf(sheet, palette));

    /// <summary>Rows stored uncompressed at the top of every sheet.</summary>
    public const int StoredRows = 256;

    public const int FrameWidth = 32;
    public const int FrameHeight = 40;
}
