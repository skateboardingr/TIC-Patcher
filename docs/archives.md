# The game's data files

Worked out from the game's own files (2026-11-19). Nothing here comes from other projects' tools or notes.

## .pac archives

`data\enhanced\*.pac` and `data\classic\*.pac`, one set per game mode, numbered 0000-0011 with per-language versions (`0004.en.pac`, `0004.fr.pac`, …).

```
0x000  "PACK", u32 header size (the data starts here), u32 file count, u8[4] flags, u64 data size
0x018  archive name, 256 bytes, XOR'd with the repeating pattern E6 12 D6 54 9A 8F 4C 53
       (aligned to the file offset)
0x118  u64 end of the entry table, u64 names offset, u64 names length
…      entries, 56 bytes each, ending at "end of the entry table":
         u32 compressed size, u8 compressed, u8 kind, u16 0, u64 size, u64 offset,
         u64 block record (0 if none), u64 name offset, u64 hash, u64 …
…      block records, 24 bytes each: u64 offset, u32 compressed size, u32 size, u32 0, u16 index, u16 file count
names  NUL-terminated file names (with folders), XOR'd with the same pattern
data   from "header size" on
```

- A file with a block record lives at `offset` inside that block once it's decompressed. Small files are packed together in one block.
- Other files are compressed on their own at `offset` (`compressed` = 1), or stored as-is (`compressed` = 0).
- Compression is **GDeflate** (DirectStorage). Every stream starts with `04 FB`, then the tile count.

The game's own `dstorage.dll` decompresses it through DirectStorage's CPU codec (`DStorageCreateCompressionCodec`), as long as `dstoragecore.dll` is loaded from the same folder first. See `src/TICPatcher.Core/Archives`.

Archive names (the XOR'd name block):

| Archive | Name | Contents |
|---|---|---|
| 0001 | char | character textures |
| 0002 (+ language) | fftpack | the classic game's files: `battle_bin.bin`, `event_unit_bin.bin` (battle units), event text `text/event*.mes`, … |
| 0004 (+ language) | nxd | 53 `.nxd` tables (item, job, ability, charaname, placename, poachitem, …) and 631 `.pzd` text files |
| 0005 | script | |
| 0010 | font | |

## .nxd tables

```
0x00  "NXDF", u32 1, u8[4] 01 02 01 00, …
0x20  u32 index offset (0x30), u32 row count
0x30  index: (u32 id, u32 offset) per row; the row starts at (offset field + offset)
…     rows (fixed or variable length), then a pool of NUL-terminated UTF-8 strings
```

Text fields are u32 offsets into the string pool. Comparing the English and French tables shows which fields are text: they're the only ones that change. The offsets count from the row start plus the first text field's position:

| Table | Rows | Row bytes | Text fields (counted from) |
|---|---|---|---|
| item.nxd | 261 | 48 | +0 name, +4 lowercase, +8 plural, +12 description, +16 name (row +0) |
| job.nxd | 174 | 64 | +4 name, +8 (second text), +12 description (row +4) |
| ability.nxd | 512 | varies | +4 name, +8 description (row +4); +16, +20 change with language but aren't plain strings |
| charaname.nxd | 1024 | 24 | +4 name (row +4) |

Not yet understood: how an item's effect line ("When attacking, has a chance of inflicting Blindness.") is reached.

## In the running game

The game keeps the `.nxd` tables in memory exactly as they are in the archive (376 tables loaded with a save open; `item.nxd` twice). Changing text there changes the game: Phoenix Down renamed to "Phoenix Up!!" in both copies showed in the item list (2026-11-19). So TIC Loader can rename things at run time, the same way it edits stats, without touching game files.
