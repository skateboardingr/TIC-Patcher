# TIC-Patcher
Mod Creation and Patching Tool for The Ivalice Chonicles in the spirit of FFT Patcher

TIC Patcher is an independent, open-source editor and mod loader for *FINAL FANTASY TACTICS - The Ivalice Chronicles*, in the spirit of [FFTPatcher](https://github.com/Glain/FFTPatcher): edit jobs, abilities, items, skill sets and more, and eventually go beyond the original game's fixed tables.

It has two parts:

- **TIC Patcher**: the editor. Mods are saved in TIC Patcher's own format, containing only the values you changed.
- **TIC Loader** (planned): a [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II) mod that applies those edits while the game runs, merging several mods, and later supports replacing data files and expanding tables (new jobs, abilities and equipment).

**No game files are modified or shipped**, and the game's DRM is left alone: edits are applied in memory at runtime.

## Status

Early research. Working so far:

- Table layouts taken from FFTPatcher, starting with jobs, with a reader/writer for their fields (numbers and flag sets).
- **TICScan**, a research tool that finds a table in a running game's memory (read-only) by comparing it with a reference copy of the original data. Rebalanced values and grown records are tolerated.
- **Confirmed in the real game:** The Ivalice Chronicles keeps the War of the Lions job table layout, and changing it in memory changes gameplay (Ramza's Move 4 → 6). See [docs/findings.md](docs/findings.md).

Next: TIC Loader's first version, a Reloaded-II mod that applies table edits from mod files when the game starts.

## Sources

Table layouts come from FFTPatcher (GPL-3.0, Joe Davidson / Glain) and the ffhacktics wiki, plus our own analysis of the running game. Each schema names its source.

## Building

```
dotnet test
```

The tests use generated data only.

## License

GPL-3.0-or-later, the same as [FFTPatcher](https://github.com/Glain/FFTPatcher). TIC Patcher is a fan project, not affiliated with Square Enix.
