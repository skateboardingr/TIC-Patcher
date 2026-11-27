using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Memory;

/// <summary>
/// A version of the game's executable, told apart by its PE header's
/// TimeDateStamp and SizeOfImage, and where each table is in it (offsets from
/// the start of the module). Measured with TICScan; see docs/tables.md.
/// </summary>
public sealed record GameBuild(string Name, uint TimeDateStamp, uint SizeOfImage, IReadOnlyDictionary<string, long> Tables)
{
    /// <summary>More places a table is kept, written with the same bytes (e.g. the game's copies of the sprite list).</summary>
    public IReadOnlyDictionary<string, long[]> Copies { get; init; } = new Dictionary<string, long[]>();

    /// <summary>Everywhere the table is: <see cref="Tables"/>'s offset first, then any copies.</summary>
    public IEnumerable<long> OffsetsOf(string table) =>
        Tables.TryGetValue(table, out var offset) ? [offset, .. Copies.GetValueOrDefault(table, [])] : [];
}

public static class GameBuilds
{
    public static readonly IReadOnlyList<GameBuild> Known =
    [
        new("Enhanced, Steam build 24304444", 0x6A5E4A1B, 0x3F9E000, new Dictionary<string, long>
        {
            [SkillSets.ActionMenus.Name] = 0x67E010,
            [SkillSets.Main.Name] = 0x67E210,
            [SkillSets.Extra.Name] = 0x67E210 + 0xB0 * 25,
            [SkillSets.JobRequirements.Name] = 0x67F390,
            [SkillSets.JobUnlockCount.Name] = 0x3486FA,
            [Jobs.FormationSprites1.Name] = 0x67F840,
            [Jobs.FormationSprites2.Name] = 0x680070,
            [Jobs.FormationSlotSprites.Name] = 0x714D50,
            [Jobs.SlotSprites.Name] = 0x7146D0,
            [Jobs.SpriteFormationSlots.Name] = 0x6829F0,
            [SkillSets.MonsterSkills.Name] = 0x67F4C0,
            [Items.ShopInventories.Name] = 0x67F890,
            [Abilities.Animations.Name] = 0x672DA0,
            [Abilities.Effects.Name] = 0x683500,
            [Jobs.Schema.Name] = 0x785E30,
            [Abilities.Common.Name] = 0x787F80,
            [Statuses.Attributes.Name] = 0x788FE0,
            [Abilities.Jump.Name] = 0x789260,
            [Abilities.Charge.Name] = 0x789280,
            [Abilities.Throw.Name] = 0x789290,
            [Abilities.Action.Name] = 0x789620,
            [Items.Common.Name] = 0x80EA90,
            [Items.Weapons.Name] = 0x80EA90 + 0xC00,
            [Items.Shields.Name] = 0x80EA90 + 0x1000,
            [Items.Armor.Name] = 0x80EA90 + 0x1020,
            [Items.Accessories.Name] = 0x80EA90 + 0x10A0,
            [Items.ChemistItems.Name] = 0x80EA90 + 0x10E0,
            [Statuses.Inflict.Name] = 0x80FBA0,
            [Items.Attributes.Name] = 0x80FEA0,
            [Items.ExtraWeapons.Name] = 0x680500,
            [Items.Extra.Name] = 0x680510,
            [Errands.Schema.Name] = 0x7B768C,
            [Errands.Eligibility.Name] = 0x2EE3B3,
            [Errands.Completion.Name] = 0x3700A9,
            [Items.ExtraArmor.Name] = 0x6732FC,
        })
        {
            Copies = new Dictionary<string, long[]>
            {
                [Jobs.FormationSprites2.Name] = [0x6807C0, 0x680C00, 0x681A90, 0x683A70],
                // The same "sprite set at least 0x4A" test in the unit lists errands use: the list
                // builder's mode 0x0A filter and the shared unit-list picker (see Errands.Eligibility).
                [Errands.Eligibility.Name] = [0x28510D, 0x2C0F41, 0x2C221D],
            },
        },
    ];

    /// <summary>The build whose PE header is at the start of <paramref name="header"/>, if known.</summary>
    public static GameBuild? Identify(ReadOnlySpan<byte> header)
    {
        if (header.Length < 0x40)
            return null;
        var pe = BitConverter.ToInt32(header[0x3C..]);
        if (pe < 0 || pe + 24 + 60 > header.Length || !header.Slice(pe, 4).SequenceEqual("PE\0\0"u8))
            return null;
        var stamp = BitConverter.ToUInt32(header[(pe + 8)..]);
        var size = BitConverter.ToUInt32(header[(pe + 24 + 56)..]);
        return Known.FirstOrDefault(b => b.TimeDateStamp == stamp && b.SizeOfImage == size);
    }
}
