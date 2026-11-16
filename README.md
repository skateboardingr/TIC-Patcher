# TIC-Patcher
Mod Creation and Patching Tool for The Ivalice Chonicles in the spirit of FFT Patcher

TIC Patcher is an independent, open-source editor and mod loader for *FINAL FANTASY TACTICS - The Ivalice Chronicles*, in the spirit of [FFTPatcher](https://github.com/Glain/FFTPatcher): edit jobs, abilities, items, skill sets and more, and eventually go beyond the original game's fixed tables.

It has two parts:

- **TIC Patcher**: the editor. Mods are saved in TIC Patcher's own format, containing only the values you changed.
- **TIC Loader**: a [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II) mod that applies those edits while the game runs, merging several mods, and later supports replacing data files and expanding tables (new jobs, abilities and equipment).

**No game files are modified or shipped**, and the game's DRM is left alone: edits are applied in memory at runtime.

## Status

Early research. Working so far:

- Table layouts taken from FFTPatcher, starting with jobs, with a reader/writer for their fields (numbers and flag sets).
- **TICScan**, a research tool that finds a table in a running game's memory (read-only) by comparing it with a reference copy of the original data. Rebalanced values and grown records are tolerated.
- **Confirmed in the real game:** The Ivalice Chronicles keeps the War of the Lions job table layout, and changing it in memory changes gameplay (Ramza's Move 4 → 6). See [docs/findings.md](docs/findings.md).
- **TIC Loader 0.1, working in the game:** when the game starts, reads `TIC/tables.json` from every enabled Reloaded-II mod, merges them in load order (later mods win field by field), finds the job table by its fingerprint and writes only the bytes that change.

Next: more tables (abilities, items, skill sets) and the editor, which will also enable mods and launch the game so players rarely need Reloaded-II's own window.

## Making a mod

A TIC mod is an ordinary Reloaded-II mod folder with no code, depending on `tic.loader`, plus a `TIC/tables.json` listing only the values it changes:

```jsonc
{
  "Jobs": {
    "0x01": { "Move": 6 },
    "0x4A": { "Equipment": "Knife, Sword, Hat, Clothing" }
  }
}
```

Jobs, abilities, skill sets, items, statuses, shops and more can be edited; [docs/tables.md](docs/tables.md) lists the table names, and the field names are in [the schemas](src/TICPatcher.Core/Tables/Schemas). A mistake (unknown field, value out of range) is reported in Reloaded-II's log and that mod is skipped; the others still load. See [examples/tic.example.ramzamove](examples/tic.example.ramzamove).

## Sources

Table layouts come from FFTPatcher (GPL-3.0, Joe Davidson / Glain), plus our own analysis of the running game. Each schema names its source.

## How this is made: AI-assisted

TIC Patcher is developed with heavy use of AI. Most of the code, the research into the game's memory and code, and the documentation were written by Claude, Anthropic's AI assistant, working under the direction of the project's maintainer, who decides what to build and tests the results in the real game. Commits made with AI help say so in a `Co-Authored-By: Claude` line.

Every finding about the game is checked against the game itself, and the docs say what is confirmed in play and what is only "likely". The projects TIC Patcher builds on, and their licenses, are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); none of them restricted AI use in the versions used.

## Credits

TIC Patcher stands on the shoulders of **[FFTPatcher](https://github.com/Glain/FFTPatcher)**, the editor that made *Final Fantasy Tactics* modding what it is:

- **Glain**, who has maintained and extended FFTPatcher and its suite (FFTacText, FFTorgASM, EntryEdit, ShishiSpriteEditor) for years.
- **Joe Davidson (melonhead)**, FFTPatcher's original author.
- **All FFTPatcher contributors.**

TIC Patcher's table layouts, field names and flag lists come from FFTPatcher, and each schema file names the FFTPatcher source it follows. Thank you.

Also built with [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II) by Sewer56 and contributors.

## Building

```
dotnet test
dotnet build src/TICLoader -c Release
```

The loader builds to `src/TICLoader/bin/Release/tic.loader`; copy that folder into Reloaded-II's `Mods` folder.

The tests use generated data only.

## License

GPL-3.0-or-later, the same as [FFTPatcher](https://github.com/Glain/FFTPatcher). Third-party licenses and the exact FFTPatcher version used are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). TIC Patcher is a fan project, not affiliated with Square Enix.
