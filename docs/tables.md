# Table map

Where FFTPatcher's tables are in *The Ivalice Chronicles* (Enhanced, `FFT_enhanced.exe`, Steam build 24304444), found by comparing a copy of the game's executable image in memory with FFTPatcher's PSP (War of the Lions) defaults. Offsets are from the start of the module. "Match" is the share of bytes equal to the PSP defaults.

Record layouts and names follow FFTPatcher (Glain, Joe Davidson); see [findings.md](findings.md) for how the job table was confirmed.

## Found

| Table | Offset | Records × size | Match | Notes |
|---|---|---|---|---|
| Action menus | +0x67E010 | 227 × 1 | 100% | FFTPatcher calls the file ActionEvents |
| Skill sets | +0x67E210 | 4475 bytes | 99.7% | |
| Job unlock requirements | +0x67F390 | 22 × 12 | 100% of these | the PSP file's last 16 bytes, JP per level, are a pointer here; JP per level not found yet |
| Monster skills | +0x67F4C0 | 240 bytes | 99.2% | |
| Job formation sprites 1 | +0x67F840 | 74 bytes | 98.6% | exact copy also at +0x1930C34 |
| Shop inventories | +0x67F890 | 512 bytes | 100% | exact copy also at +0x1930644; which one the game reads is untested |
| Job formation sprites 2 | +0x680070 | 148 bytes | 100% | also at +0x6807C0, +0x680C00 |
| Ability animations | +0x672DA0 | 512 × 3 | 90.9% | values changed in the remaster |
| Ability effects | +0x683500 | 908 bytes | 99.8% | |
| Item effects | +0x6837E0 | 28 bytes | 100% | |
| Reaction effects | +0x68384C | 64 bytes | 100% | |
| **Jobs** | +0x785E30 | 169 × 49 | 98.2% | confirmed live |
| Abilities, common | +0x787F80 | 512 × 8 | 99.9% | |
| Status attributes | +0x788FE0 | 40 × 16 | 100% | |
| Jump (horizontal/vertical) | +0x789260 | 12 × 2 | 100% | |
| Charge | +0x789280 | 8 × 2 | differs | looks rebalanced (e.g. Charge+1 takes 3, not 4); to confirm |
| Throw | +0x789290 | 12 × 1 | 100% | |
| **Abilities, action** | +0x789620 | 0x170 × **20** | 99.8% | PSP records are 14 bytes; the first 14 match, 6 bytes are new (`00 00 FF FF FF FF` on empty ones) |
| Propositions (errands) | +0x7B768C | 2684 bytes | 100% | |
| Items, common | +0x80EA90 | 256 × 12 | 99.8% | |
| Items, weapon/armor/etc. data | +0x80F690 | 1290 bytes | 100% | right after the common data, as in the PSP file |
| Inflict statuses | +0x80FBA0 | 128 × 6 | 100% | |
| **Item attributes** | +0x80FEA0 | 0x50 × **26** | 100% of the first 25 | PSP records are 25 bytes; one byte is added at the end |

## Names in tables.json

`Jobs`, `JobRequirements`, `SkillSets` (0x00-0xAF), `SkillSetsExtra` (0xE0-0xE2), `MonsterSkills` (0xB0-0xDF), `ActionMenus`, `Abilities` (common data, 0x000-0x1FF), `ActionAbilities` (0x000-0x16F), `ThrowAbilities`, `JumpAbilities`, `ChargeAbilities`, `AbilityEffects`, `AbilityAnimations`, `Items` (0x00-0xFF), `Weapons`, `Shields`, `Armor`, `Accessories`, `ChemistItems`, `ItemAttributes`, `ShopInventories`, `StatusAttributes`, `InflictStatuses`.

Record ids are the game's own: armor starts at 0x90, charge abilities at 0x196 and so on. Fields are listed in `src/TICPatcher.Core/Tables/Schemas`. Check a table against a memory copy with `TICScan --image <file> <table> <id>...`.

Checked against well-known values in a memory copy (2026-11-16): Dagger price 100, weapon power 3; Potion 50, Phoenix Down 300; Leather Helm +10 HP; Escutcheon 10/3 block; Cure and Fire range 4, CT 4, 6 MP; Haste CT 2, 8 MP; Chemist's skill set is the item abilities 368-381; Knight needs Squire level 2. Live edits through TIC Loader confirmed in the game (2026-11-16): Potion price 50 → 10 in shops, Cure MP 6 → 1 in the ability menu (not yet tried in battle), alongside Ramza's Move 6. The other tables are decoded and located the same way but not yet tried live.

## Not found in the executable

- **JP needed per job level**: the PSP values (100, 200, 400 ... 3000) aren't in the executable as 16-bit numbers.

- **PSP-only items and item attributes** (ids 0x100 and up) and **poaching**: no match at any record size from 12 to 36 bytes. Probably loaded from the game's data files at run time, or not present.
- **Move-find items** and **ENTD** (battle unit setups): not in the executable; most likely in the data files.
- **Item, arithmetick and "other" ability tables**: too short and generic to place from their bytes alone.

These come with the next stage, reading the game's data files.
