# Table map

Where FFTPatcher's tables are in *The Ivalice Chronicles* (Enhanced, `FFT_enhanced.exe`, Steam build 24304444), found by comparing a copy of the game's executable image in memory with FFTPatcher's PSP (War of the Lions) defaults. Offsets are from the start of the module. "Match" is the share of bytes equal to the PSP defaults.

Record layouts and names follow FFTPatcher (Glain, Joe Davidson); see [findings.md](findings.md) for how the job table was confirmed.

## Found

| Table | Offset | Records × size | Match | Notes |
|---|---|---|---|---|
| Ability action events | +0x67E010 | 227 bytes | 100% | |
| Skill sets | +0x67E210 | 4475 bytes | 99.7% | |
| Job levels (JP) | +0x67F390 | 280 bytes | 94.3% | values changed in the remaster |
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

## Not found in the executable

- **PSP-only items and item attributes** (ids 0x100 and up) and **poaching**: no match at any record size from 12 to 36 bytes. Probably loaded from the game's data files at run time, or not present.
- **Move-find items** and **ENTD** (battle unit setups): not in the executable; most likely in the data files.
- **Item, arithmetick and "other" ability tables**: too short and generic to place from their bytes alone.

These come with the next stage, reading the game's data files.
