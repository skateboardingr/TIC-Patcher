# Writing a bridge for TIC Loader

A bridge teaches TIC Loader to read mods made in another format, so players can use those mods without converting them. TIC Loader handles everything else: load order, merging with other mods, checking the game version, writing the game's memory safely and logging. A bridge only answers one question: *"what table edits are in this mod's folder?"*

[`examples/TICFormatSample`](../examples/TICFormatSample) is a complete working bridge, about 80 lines. Copy it and change `CsvFormat.Read`.

## The pieces

1. **A Reloaded-II mod** whose `ModConfig.json` lists `"tic.loader"` in `ModDependencies`. TIC Loader then starts first.
2. **A reference to `TICLoader.Interfaces`** (`src/TICLoader.Interfaces`; a project reference with `Private="false"`, since TIC Loader supplies it at run time), and to `Reloaded.Mod.Interfaces` with `ExcludeAssets="runtime"`.
3. **In `StartEx`**, get the API and register your format:

   ```csharp
   if (modLoader.GetController<ITICLoader>() is { } c && c.TryGetTarget(out var tic))
       tic.AddModFormat(new MyFormat());
   ```

4. **An `ITICModFormat`.** For each enabled mod, in load order, TIC Loader calls `Read(modId, modFolder)`:
   - Return `null` if the folder isn't a mod in your format.
   - Otherwise return its edits as `TICFieldEdit(Table, Id, Field, Value)`.
   - For a broken mod, throw `FormatException` with a clear message. TIC Loader logs it and skips just that mod.

## Mapping another format

- **Tables and fields:** `tic.Tables` lists every table TIC Loader can edit, with each field's byte offset, size and kind, and flag bit names. Map the other format's names to these. [tables.md](tables.md) lists the table names, and the field names are in `src/TICPatcher.Core/Tables/Schemas`.
- **Record ids** are the game's own (armor starts at 0x90, charge abilities at 0x196, the remaster's extra items at 0x100).
- **Values** are text: a number, or flag names joined by `", "` (`"Knife, Sword"`).
- **Bytes without a name:** use the field `"+N"` for the raw byte at offset N of the record (value 0-255). Raw bytes apply after named fields, so they can refine one. If the other format addresses data by offset, this maps it directly.
- **Edits from code:** `tic.AddEdits(modId, edits)` adds edits as if they came from that mod. Use it when your bridge computes changes rather than reading them.
- **After applying:** `tic.TablesApplied` fires once the edits are in the game's memory, and `tic.GameVersion` names the recognised version (null if unknown).

## Checking your bridge

- Enable your bridge, TIC Loader and a mod in the other format, then launch through Reloaded-II. The console shows `<mod id>: N change(s) from <your format name>`, then a line for each table written.
- Mistakes show as `<mod id>: skipped <format>, <message>`, with the table, record, field and value that was wrong.
- `TICScan --image` (or the TIC Patcher editor) shows the original values to compare against.

## Ground rules

- Read only the other format's **files**, the way a player's mods are laid out on disk. Respect the other project's license and wishes. If its author doesn't want their code used in some way, a bridge mustn't use it that way.
- Don't write the game's memory yourself; return edits and let TIC Loader apply them. That keeps the version check and the log in one place.
