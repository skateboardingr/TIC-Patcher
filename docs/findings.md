# Research findings

Our own measurements on *FINAL FANTASY TACTICS - The Ivalice Chronicles* (Steam, Windows), plus where each layout comes from. Offsets are for the build named; other builds may differ, which is why TIC Patcher finds tables by content rather than by fixed address.

## Job table

- **Layout: War of the Lions (PSP), 169 records of 49 bytes**, as documented by FFTPatcher (`Datatypes/Job/Job.cs`). See `src/TICPatcher.Core/Tables/Schemas/Jobs.cs`.
- **Location, build 24304444 (Enhanced, `FFT_enhanced.exe`):** inside the executable image at **+0x785E30**, in read-only memory.
- **Match with FFTPatcher's PSP defaults:** 99.9% on the stable fields (skill set, Move, Jump, C-Evade, elements, monster graphics), found by `TICScan` (2026-11-15).
- **Remaster differences seen so far:** generic and Ramza jobs gained equipment types, e.g. Squire and Chemist can also equip hair adornments and perfume.
- **Live use confirmed:** setting Move to 6 on Ramza's jobs (0x01–0x03) in memory made Ramza's status screen show Move 6 (2026-11-15, chapter 1 save, Enhanced).

## How it was found

`tools/TICScan` reads the running game's memory (read-only unless `--set` is used) and scores every position for how well 169 records at a given record size match the stable fields of a reference copy of the original table. This tolerates rebalanced values and grown records. Writes (`--set`) unprotect only the written bytes' page, write, and restore the protection; nothing persists after the game closes.
