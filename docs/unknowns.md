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
- **Item attributes, byte 25** (new in TIC): 0 on all 80.
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
