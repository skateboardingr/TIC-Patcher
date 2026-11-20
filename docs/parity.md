# Roadmap: FFTPatcher for The Ivalice Chronicles

TIC Patcher is to The Ivalice Chronicles what FFTPatcher was to the PSX and PSP versions: it edits what TIC actually has, and it feels familiar to anyone who used FFTPatcher. FFTPatcher's pages are a guide to what modders expect, not a checklist to copy:

- **Keep** the familiar pages and editing experience, for everything TIC still has.
- **Drop** what only made sense for PSX/PSP: War of the Lions items TIC doesn't have, ISO and savestate patching, cheat codes.
- **Add** what's new in TIC: the remaster's own items and fields, TIC Loader and its Play button, and features FFTPatcher never had (below).

✓ done, ◐ partly, ✗ not yet.

## Editor pages

| FFTPatcher page | TIC Patcher | Notes |
|---|---|---|
| Jobs | ✓ | |
| Job Levels | ◐ | unlock requirements ✓; JP per level not found in the executable yet |
| Skill Sets | ✓ | |
| Monster Skills | ✓ | |
| Action Menus | ✓ | numbers only; menu names still to come |
| Abilities | ◐ | common, action, throw, jump, charge ✓; item, arithmetick and "other" tables not located |
| Animations | ✓ | as raw bytes, as in FFTPatcher |
| Ability effects | ✓ | numbers only; effect names still to come |
| Items | ✓ | plus the remaster's extra items (Akademy set, Ring of Aptitude, Materia Blade+) |
| Item Attributes | ✓ | including the remaster's JP Boost byte |
| Status Effects | ✓ | |
| Inflict Statuses | ✓ | |
| Shop Inventories | ✓ | by shop (pick a town, tick what it sells) or by item |
| Errands (Propositions) | ◐ | the 96 errands ✓; the bonus and price tables after them still to add |
| Poaching | ✗ | not in the executable: data-file stage |
| Move-Find Items | ✗ | not in the executable: data-file stage |
| ENTD (battles and their units) | ✗ | not in the executable: data-file stage |

## Editor features

| Feature | TIC Patcher |
|---|---|
| Original value on hover, revert a record | ✓ |
| Names in lists and for skill sets / abilities | ✓ TIC's own names, read from the game |
| Edit names and descriptions (items, jobs, abilities, skill sets, units) | ✓ |
| Dropdowns with names for formulas, item types, menu actions, effects, ability types, shop availability | ✓ |
| Inflict status dropdown that shows which statuses each entry inflicts | ✓ |
| Copy and paste records | ✓ |
| Show only changed records | ✓ |
| Consolidate inflict statuses / item attributes (FFTPatcher utilities) | ✗ |
| Open and save a mod | ✓ (`tables.json`) |
| Save as a ready Reloaded-II mod, enable it and launch the game ("Play") | ✓ |
| Mods tab: turn installed mods on and off, open TIC mods in the editor | ✓ |
| TIC Loader built in, installed or updated by Play | ✓ |
| Import an FFTPatcher patch file (.fftpatch, US PSP or PSX) | ✓ only what the patch changed (compared with FFTPatcher's defaults, read from the user's FFTPatcher folder), on top of TIC's values; flags bit by bit. Not yet: poaching, move-find, battles, War of the Lions' extra items |
| Extension API for other mods (bridges to other mod formats) | ✓ ([bridge.md](bridge.md)) |

## Not needed

ISO and savestate patching, CWCheat and cheat.db generation, fftpack tools: TIC Patcher's mods are applied by TIC Loader at run time, so the game's files are never patched.

Text (FFTacText's job) is done differently: names and descriptions of items, jobs, abilities, skill sets and units are edited beside their stats and applied by TIC Loader, never by patching files.

## Beyond FFTPatcher

Features FFTPatcher never had, planned for after parity. The hard ones need TIC Loader to change the game's code, not just its tables:

- **New jobs:** create classes of your own, e.g. a Black Knight unlocked by Black Mage 8 and Knight 8. The job table has only two empty slots, so this needs room in the tables, plus names, sprites, menus and unlock rules.
- **War of the Lions leftovers:** TIC's job table still has full records for Dark Knight (0xA0) and Onion Knight (0xA1, 0xA4), still allowed to equip Fell Swords and Lip Rouge, though no item of either type exists any more (TIC's items stop at 0x104). Bringing these jobs back is a natural first test for new jobs: the stats exist; unlock rules, menus, sprites and names would need checking.
- **New abilities, items and skill sets** beyond the fixed table sizes. The remaster's action ability table seems to have room past the last ability, which may be a first place for this.
- **Animation builder:** assemble an ability's animation from named parts in dropdowns (a charge-up, a weapon swing, a hit effect), using the animation and effect ids abilities already have. This needs a catalogue of what each id looks like, which the community could help fill in.
- **More equip slots:** change how many action, reaction, support and movement abilities a unit can have. This is code plus the unit and save formats, so it needs care with saves. (Today, a job's four innate abilities already give always-on extras.)
- **New ability and item behaviours** stored in bytes the game never reads (status attributes' Blank1 and Blank2) or in the remaster's still-unknown ability bytes, with TIC Loader adding the behaviour in code. Examples: costs HP instead of MP, inflicts a second status.
- **Sprite editing, and adding sprites to the roster:** TIC Patcher's answer to FFTPatcher's sprite tools (ShishiSpriteEditor). The character textures live in the game's `.pac` archives (see docs/archives.md).
- **Play button and mod manager:** install, enable and launch from the editor, so Reloaded-II's own window is rarely needed.
