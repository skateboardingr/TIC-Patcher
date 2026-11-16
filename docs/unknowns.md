# Working out the unknowns

FFTPatcher names many bits and bytes "Unknown". These notes record what *The Ivalice Chronicles'* own data says about them. The method: for each unknown bit, list exactly which records set it, and measure how often it agrees with settings we do understand. Nothing here has been tested in the game yet. "Likely" means strong agreement in the data.

The editor shows these notes as hover hints, and the flags they describe are in italics. Field names are unchanged, so existing mods keep working. Source: `src/TICPatcher.Core/Tables/FieldNotes.cs`.

## Ability AI flags (Abilities, bytes 4-6)

Compared over the 366 action abilities that have a formula.

| Flag | Set on | Best match | Reading |
|---|---|---|---|
| Unknown1 | 104 | NotSilence 93% | likely: magic that Silence blocks |
| Unknown2 | 117 | costs MP 98% | likely: affected by Faith. The FFH forums report the same. |
| Unknown3 | 74 | Shirahadori 92%, Evadeable 83% | likely: can be evaded. The FFH forums report the same. |
| Unknown4 | 17 | Direct **100%** | AI copy of Direct |
| Unknown5 | 3 | Kiku-ichimonji, Shockwave, Divine Ruination | line attacks that pass through units |
| NotUnknown6 | 8 | the breaths and Ink | breath-style line attacks; with Unknown5, it covers almost all LinearAttack abilities |
| Unknown7 | 1 | Tri-Attack only | three-direction shape |
| Unknown8 | 1 | Tri-Breath only | three-direction shape |
| Unknown9 | 154 | harmful magic and katana draws | not settled |
| Unknown10 | 94 | physical skills | not settled |

These look like hints for the enemy AI: copies of an ability's real settings in a form the AI reads quickly. If that's right, a mod that changes an ability's targeting or element should update these too, or enemies will misjudge it. That's a good job for the editor: offer to keep them in sync.

## Ability flags (Abilities, byte 7)

| Flag | Set on | Best match | Reading |
|---|---|---|---|
| Unknown2 | 19 | NotHitEnemies 98% | likely: only affects allies (support summons, songs, Steal Gil/EXP) |
| Unknown3 | 42 | NotHitAllies 96% | likely: only affects enemies (attack summons, katana draws) |
| Unknown4 | 80 | physical skills | not settled |

## Status flags (StatusAttributes, bytes 4-5)

| Flag | Set on | Reading |
|---|---|---|
| Flags1 Unknown1, Unknown5 | Crystal, Treasure | the unit has left the battle |
| Flags1 Unknown2 | Defending, Performing | likely: ends when the unit acts |
| Flags1 Unknown3 | Poison, Regen | likely: changes HP at the end of each turn |
| Flags1 Unknown4 | Confusion, Invisible, Charm, Sleep | statuses an attack can break; not settled |
| Flags1 Unknown6 | Crystal, Traitor, Treasure | not settled |
| Flags2 Unknown8 | Crystal, Dead, Undead, Jump, Stone, Traitor, Treasure, Critical | not settled |
| Flags2 Unknown9 | Blind, Confusion, Silence, Invisible, Berserk, Chicken, Toad, Stop, Charm, Sleep, Immobilize, Disable | not settled |

## Never used by the original data

The original data gives no clue for these:

- **Action abilities, bytes 14-19** (new in TIC): `00 00 FF FF FF FF` on all 368. They look like a 2-byte value and a 4-byte "none" (-1), probably a remaster feature the original abilities don't use.
- **Item attributes, byte 25** (new in TIC): 0 on all 80. *Solved below: JP Boost from equipment.*
- **Weapons, Unknown (byte 3)**: FF on all 128.
- **Items, Unknown1 and Unknown2**: 0 on all 256.
- **Status attributes, Blank1 and Blank2**: 0 on all 40.

### First experiment (2026-11-16)

TIC Loader applied the changes (confirmed in the log), and Ramza used the abilities in one battle:

- Focus, `New14` 0 → 1: no visible change.
- Rush, `New16`-`New19` FF FF FF FF → 00 00 00 00: no visible change.
- Stone, unchanged, used for comparison: normal.

This is one quick look, not exhaustive. The bytes may matter in situations not tried: other ability types (magic, summons), enemies using the ability, menus, or other values. Next tries: magic (Cure), larger values, and an enemy-used ability. If they keep doing nothing, TIC Loader could give them a meaning of its own.

## Watching what the game reads

TIC Loader can record which bytes of chosen tables the game actually reads, and from which code. Put a `watch.json` next to `tic.loader.dll`:

```json
{ "Tables": ["ActionAbilities", "ItemAttributes"], "Minutes": 10 }
```

Then play normally. `watch-report.txt` appears next to it, updated every 30 seconds, with a read count per byte of each record and the code addresses that read it. A byte that stays at 0 through menus, battles and enemy turns is very likely unused. The game runs slower while watching. Delete `watch.json` to turn it off. `tools/WatchTest` checks the watcher outside the game.

### First watch (2026-11-16): menus, the field and a battle

- **ItemAttributes byte 25 is read.** The routine that applies equipment attributes to a unit (`+0x277A31`-`+0x277B74`) does `cmp [attr+0x19], 0` / `bts [unit+0x1F8], 11`: a non-zero byte 25 turns on bit 11 of a unit flags field. That bit is checked in two places only:
  - `+0x2835E0`, in the JP-gain routine (it loops over the 21 units and shares a quarter of the JP with the others). JP Boost (`test byte [unit+0x99], 0x40`) multiplies by 1.5; otherwise bit 11 multiplies by the float at `+0x722E0C`, which is **1.5**.
  - `+0x30C6D5`, in the Accrue EXP/JP (move-to-earn) routine. The JP amount is doubled by JP Boost, or otherwise by bit 11.

  So byte 25 means **"this equipment gives JP Boost"**, a remaster feature no original item uses. The field is now named `JPBoost`. Next: confirm in play.
- **StatusAttributes Blank1 and Blank2 are never read**, while every other byte is. They're free for future TIC Loader features.
- **ActionAbilities**: one routine (`+0x5C9F50`-`+0x5C9F8E`) reads every record once, 4 bytes at a time: a copy of the table. Later reads go to the copy, so this watch says nothing yet about bytes 14-19. Next: find and watch the copy.
- **Abilities (common)**: the AI reads byte 4 heavily (`+0x320805`) and byte 5 once per ability (`+0x38AC87`). The report shows where reads start, not their width, so byte 6 may be covered by wider reads. The code at those addresses (`tools/Disasm`) settles it.

The unit's support flags include `[unit+0x99] & 0x40` = JP Boost.

### Bonus items (2026-11-16)

The user pointed out that the preorder bonus items had JP and EXP boosts and that their save has them. The item attribute table turned out to have 85 entries, not the PSP's 80. The extra five (0x50-0x54) belong to the remaster's bonus items:

| Attributes | Effect | Item (+0x680510, 12-byte records) | Name in game |
|---|---|---|---|
| 0x54 | MA +4 | 0x100 sword, power 16 | **Materia Blade+** (identified by the user; another edition's bonus, not in their save; exact spelling unchecked) |
| 0x51 | Speed +1 | 0x101 sword, power 6, evade 10 | **Akademy Blade** |
| 0x52 | immune to Charm | 0x102 hat (type 21) | **Akademy Beret** |
| 0x53 | permanent Shell | 0x103 clothing (type 24) | **Akademy Tunic** |
| 0x50 | **JPBoost = 1** | 0x104 ring (type 28) | **Ring of Aptitude** |

Names, stats and effects were confirmed from the in-game item pages (screenshots from the user, 2026-11-16). `ItemType` follows the equipment list (3 Sword, 21 Hat, 24 Clothing, 28 Ring). The beret and tunic both have the Body flag set, yet the beret equips only on the head (checked in game). So the equip slot follows `ItemType`, and the Flags byte doesn't decide it for these items. Their +20 HP is in armor data not yet located for the bonus items.

Their weapon data is just before them (+0x680500, 8-byte records: range 1, power 16 and 6, evade 10). A list of item-id boundaries nearby (…0xD0, 0xF0, 0x100…) suggests ids 0x100-0x104. The Ring of Aptitude's in-game description ("increases the amount of JP and Job EXP earned during battle… does not multiply with other traits that provide the same benefit. (Bonus item)") matches byte 25's code: JP x1.5, not stacking with JP Boost. "Job EXP" is probably JP; no separate EXP-boost check has turned up. If one of these gives EXP Boost, it's done another way. Next: names, and checking these in play.
