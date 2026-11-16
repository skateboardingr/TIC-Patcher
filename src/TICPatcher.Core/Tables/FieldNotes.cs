namespace TICPatcher.Core.Tables;

/// <summary>
/// What we've learned about fields and flags FFTPatcher calls Unknown, shown
/// as hints in the editor. Names stay as they are so mods keep working.
/// "Likely" means the data agrees strongly but it hasn't been tested in the
/// game; see docs/unknowns.md for the evidence.
/// </summary>
public static class FieldNotes
{
    private static readonly Dictionary<string, string> Notes = new(StringComparer.Ordinal)
    {
        ["Abilities.AIFlags.Unknown1"] = "Likely: AI hint that the ability is magic that Silence blocks (agrees with the ability's own NotSilence setting on 93% of abilities).",
        ["Abilities.AIFlags.Unknown2"] = "Likely: AI hint 'affected by Faith' (set on 98% of abilities that cost MP; also reported on the FFH forums).",
        ["Abilities.AIFlags.Unknown3"] = "Likely: AI hint 'can be evaded' (follows physical, evadable skills; also reported on the FFH forums).",
        ["Abilities.AIFlags.Unknown4"] = "Likely: AI copy of the ability's Direct setting (agrees on 100% of abilities).",
        ["Abilities.AIFlags.Unknown5"] = "Likely: AI hint for line attacks that go through units (Kiku-ichimonji, Shockwave, Divine Ruination only).",
        ["Abilities.AIFlags.NotUnknown6"] = "Likely: AI hint for breath-style line attacks (the breaths and Ink only).",
        ["Abilities.AIFlags.Unknown7"] = "Only Tri-Attack has this: an AI hint for its three-direction shape.",
        ["Abilities.AIFlags.Unknown8"] = "Only Tri-Breath has this: an AI hint for its three-direction shape.",
        ["Abilities.AIFlags.Unknown9"] = "Set on harmful magic and katana draws (154 abilities); meaning not settled.",
        ["Abilities.AIFlags.Unknown10"] = "Set on physical skills (94 abilities, close to Shirahadori-blockable); meaning not settled.",
        ["Abilities.Flags2.Unknown2"] = "Likely: 'only affects allies' (agrees with NotHitEnemies on 98%: support summons, songs).",
        ["Abilities.Flags2.Unknown3"] = "Likely: 'only affects enemies' (agrees with NotHitAllies on 96%: attack summons, katana draws).",
        ["Abilities.Flags2.Unknown4"] = "Set on physical skills (80 abilities); meaning not settled.",
        ["StatusAttributes.Flags1.Unknown3"] = "Only Poison and Regen: likely 'changes HP at the end of each turn'.",
        ["StatusAttributes.Flags1.Unknown2"] = "Only Defending and Performing: likely 'ends when the unit acts'.",
        ["StatusAttributes.Flags1.Unknown1"] = "Only Crystal and Treasure: the unit has left the battle.",
        ["StatusAttributes.Flags1.Unknown5"] = "Only Crystal and Treasure: the unit has left the battle.",
        ["StatusAttributes.Flags1.Unknown4"] = "Confusion, Invisible, Charm, Sleep: statuses an attack can break; not settled.",
        ["StatusAttributes.Flags2.Unknown9"] = "Blind, Confusion, Silence, Invisible, Berserk, Chicken, Toad, Stop, Charm, Sleep, Immobilize, Disable; meaning not settled.",
        ["ActionAbilities.New14"] = NewAbilityBytes,
        ["ActionAbilities.New15"] = NewAbilityBytes,
        ["ActionAbilities.New16"] = NewAbilityBytes,
        ["ActionAbilities.New17"] = NewAbilityBytes,
        ["ActionAbilities.New18"] = NewAbilityBytes,
        ["ActionAbilities.New19"] = NewAbilityBytes,
        ["ItemAttributes.JPBoost"] = "New in The Ivalice Chronicles: non-zero gives the wearer JP Boost (JP from actions x1.5, Accrue JP doubled; doesn't stack with the support ability). Read from the game's code; not yet tested in play. 0 on every original item.",
        ["StatusAttributes.Blank1"] = "Never read by the game (watched 2026-11-16): free for future TIC Loader features.",
        ["StatusAttributes.Blank2"] = "Never read by the game (watched 2026-11-16): free for future TIC Loader features.",
        ["Weapons.Unknown"] = "FF on every weapon in the original game.",
        ["Items.Unknown1"] = "0 on every item in the original game.",
        ["Items.Unknown2"] = "0 on every item in the original game.",
    };

    private const string NewAbilityBytes =
        "New in The Ivalice Chronicles (records grew from 14 to 20 bytes); 00 00 FF FF FF FF on every ability. Purpose unknown: experiment carefully.";

    public static string? For(string table, string? field = null, string? flag = null) =>
        Notes.GetValueOrDefault(string.Join('.', new[] { table, field, flag }.Where(p => p is not null)));
}
