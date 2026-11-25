using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Sprites;

/// <summary>
/// Which story sprites can be in the party, and giving one a formation slot so it can.
/// A unit with a story sprite (sprite set below 0x80) gets its slot from
/// <see cref="Jobs.SpriteFormationSlots"/>; the party screen draws slots below 0x18 from
/// <see cref="Jobs.FormationSlotSprites"/> and crashes on the guests' 0x78-0x93. So a sprite
/// joins by taking a spare story slot (one whose character never joins) and pointing it back at
/// itself. Generic and monster sprites go through their jobs instead.
/// </summary>
public static class FormationSlots
{
    /// <summary>Slots 0x00-0x17 are the story characters'.</summary>
    public const int StorySlots = 0x18;

    /// <summary>Story sprites (the generic and monster sheets start at 0x60).</summary>
    public static bool Applies(int sprite) => sprite is > 0 and < 0x60;

    /// <summary>
    /// Story slots to lend, best first: 08 belongs to no sprite, then characters who never join
    /// (Tietra, dead Marach, Orran, Simon, Delita ch1, Argath, Alma, Ovelia). Lending one changes
    /// that character's formation picture, which only matters if they're in the party.
    /// </summary>
    public static readonly IReadOnlyList<int> Spare = [0x08, 0x10, 0x09, 0x0C, 0x0A, 0x03, 0x04, 0x15, 0x0B, 0x05];

    public static int SlotOf(ModProject project, int sprite) =>
        int.Parse(project.Current(Jobs.SpriteFormationSlots, sprite, "Slot"));

    public static int SpriteIn(ModProject project, int slot) =>
        int.Parse(project.Current(Jobs.FormationSlotSprites, slot, "Sprite"));

    /// <summary>The sprite has a story slot, so the party screen can show it.</summary>
    public static bool InParty(ModProject project, int sprite) => SlotOf(project, sprite) < StorySlots;

    /// <summary>The story slot showing this sprite on the formation screen, if any.</summary>
    public static int? FormationSlot(ModProject project, int sprite) =>
        Enumerable.Range(0, StorySlots).Cast<int?>().FirstOrDefault(s => SpriteIn(project, s!.Value) == sprite);

    /// <summary>
    /// Lets the sprite join: a slot already showing it, or the first spare slot nobody has
    /// taken in this mod, is pointed at it both ways. Returns the slot, or null if none is left.
    /// </summary>
    public static int? AddToParty(ModProject project, int sprite)
    {
        var slot = FormationSlot(project, sprite) ?? Spare.Cast<int?>().FirstOrDefault(s => !Taken(project, s!.Value));
        if (slot is not { } chosen)
            return null;
        project.Set(Jobs.FormationSlotSprites, chosen, "Sprite", sprite.ToString());
        project.Set(Jobs.SpriteFormationSlots, sprite, "Slot", chosen.ToString());
        // The face and the other screens go by the sprite list; point it at a slot showing this
        // sprite if it doesn't already (Wiegraf ch. 2/3's goes to Valmafra in the game's data).
        if (FaceSlot(project, sprite) is null
            && Enumerable.Range(0, Jobs.SlotSprites.RecordCount).Cast<int?>().FirstOrDefault(s => SlotSprite(project, s!.Value) == sprite) is { } face)
            project.Set(Jobs.FormationSprites2, sprite, "FormationSprite2", face.ToString());
        return chosen;
    }

    /// <summary>The sprite list's slot for this sprite, if that slot shows it (it picks the party-screen face).</summary>
    public static int? FaceSlot(ModProject project, int sprite)
    {
        var slot = int.Parse(project.Current(Jobs.FormationSprites2, sprite, "FormationSprite2"));
        return slot < Jobs.SlotSprites.RecordCount && SlotSprite(project, slot) == sprite ? slot : null;
    }

    private static int SlotSprite(ModProject project, int slot) =>
        int.Parse(project.Current(Jobs.SlotSprites, slot, "Sprite"));

    /// <summary>Puts the sprite's slot back as the game has it, and frees a slot it was lent.</summary>
    public static void RemoveFromParty(ModProject project, int sprite)
    {
        var slot = SlotOf(project, sprite);
        project.Revert(Jobs.SpriteFormationSlots, sprite);
        if (slot < StorySlots && project.IsChanged(Jobs.FormationSlotSprites, slot) && SpriteIn(project, slot) == sprite
            && !Enumerable.Range(1, Jobs.SpriteFormationSlots.RecordCount - 1).Any(s => s != sprite && project.IsChanged(Jobs.SpriteFormationSlots, s) && SlotOf(project, s) == slot))
            project.Revert(Jobs.FormationSlotSprites, slot);
    }

    // A slot this mod already lent: its picture was changed, or an edited sprite points at it.
    private static bool Taken(ModProject project, int slot) =>
        project.IsChanged(Jobs.FormationSlotSprites, slot)
        || Enumerable.Range(0, Jobs.SpriteFormationSlots.RecordCount).Any(s => project.IsChanged(Jobs.SpriteFormationSlots, s) && SlotOf(project, s) == slot);
}
