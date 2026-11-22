namespace TICPatcher.Core.Tables.Schemas;

/// <summary>
/// Unit sprite palettes: each battle sprite sheet's 16 palettes of 16 colours (the first
/// 0x200 bytes of unit/battle_NAME_spr.bin, see <see cref="Core.Sprites.UnitSprite"/>). Record
/// sprite * 16 + palette; a colour is 5 bits each of red, green and blue (bit 15 unused),
/// and colour 0 is see-through. Read from the game's files like the battles.
/// </summary>
public static class SpriteSheets
{
    /// <summary>Sprite ids 0x01 to 0x9A (the PSX game's battle sprites).</summary>
    public const int Count = 0x9A;

    public static readonly TableSchema Palettes = new(
        "SpritePalettes",
        RecordSize: 32,
        RecordCount: Count * 16,
        Fields: Enumerable.Range(0, 16).Select(i => FieldDef.U16($"Color{i}", i * 2)).ToList(),
        Source: "TIC's sprite sheets (data\\enhanced\\0002.pac); sprite order from FFTPatcher ShishiSpriteEditor/DataTypes/Sprites/MemoryLocs.txt")
    {
        FirstId = 16,
        InExecutable = false,
        RecordName = id => $"{Names.Get(Names.SpriteFiles, id / 16) ?? $"Sprite {id / 16:X2}"}, palette {id % 16}",
    };
}
