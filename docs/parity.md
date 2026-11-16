# FFTPatcher parity

The goal: everything FFTPatcher can edit for War of the Lions, editable for The Ivalice Chronicles, in a tool that's pleasant to use. ✓ done, ◐ partly, ✗ not yet.

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
| Shop Inventories | ◐ | editable per item (which shops sell it); FFTPatcher's per-shop view still to come |
| Errands (Propositions) | ✗ | found in the executable (`+0x7B768C`); layout still to add |
| Poaching | ✗ | not in the executable: data-file stage |
| Move-Find Items | ✗ | not in the executable: data-file stage |
| ENTD (battles and their units) | ✗ | not in the executable: data-file stage |

## Editor features

| Feature | TIC Patcher |
|---|---|
| Original value on hover, revert a record | ✓ |
| Names in lists and for skill sets / abilities | ✓ |
| Dropdowns with names for formulas, item types, menu actions, effects, inflict statuses, shop availability | ✗ |
| Copy and paste records | ✗ |
| Show only changed records | ✗ |
| Consolidate inflict statuses / item attributes (FFTPatcher utilities) | ✗ |
| Open and save a mod | ✓ (`tables.json`) |
| Save as a ready Reloaded-II mod, enable it and launch the game ("Play") | ✗ |
| Import an FFTPatcher patch file | ✗ |
| Extension API for other mods (bridges to other mod formats) | ✓ ([bridge.md](bridge.md)) |

## Not needed

ISO and savestate patching, CWCheat and cheat.db generation, fftpack tools: TIC Patcher's mods are applied by TIC Loader at run time, so the game's files are never patched.

Text (names and descriptions, FFTacText's job) is a separate future step.

## Beyond FFTPatcher

Features FFTPatcher never had, planned for after parity. The hard ones need TIC Loader to change the game's code, not just its tables:

- **New jobs:** create classes of your own, e.g. a Black Knight unlocked by Black Mage 8 and Knight 8. The job table has only two empty slots, so this needs room in the tables, plus names, sprites, menus and unlock rules.
- **New abilities, items and skill sets** beyond the fixed table sizes. The remaster's action ability table seems to have room past the last ability, which may be a first place for this.
- **Animation builder:** assemble an ability's animation from named parts in dropdowns (a charge-up, a weapon swing, a hit effect), using the animation and effect ids abilities already have. This needs a catalogue of what each id looks like, which the community could help fill in.
- **More equip slots:** change how many action, reaction, support and movement abilities a unit can have. This is code plus the unit and save formats, so it needs care with saves. (Today, a job's four innate abilities already give always-on extras.)
- **New ability and item behaviours** stored in bytes the game never reads (status attributes' Blank1 and Blank2) or in the remaster's still-unknown ability bytes, with TIC Loader adding the behaviour in code. Examples: costs HP instead of MP, inflicts a second status.
- **Play button and mod manager:** install, enable and launch from the editor, so Reloaded-II's own window is rarely needed.
