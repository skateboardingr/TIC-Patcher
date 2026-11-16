namespace TICLoader.Interfaces;

/// <summary>
/// TIC Loader's API for other Reloaded-II mods. Get it in your mod's StartEx:
/// <code>
/// _modLoader.GetController&lt;ITICLoader&gt;()?.TryGetTarget(out var tic);
/// tic?.AddModFormat(new MyFormat());
/// </code>
/// Your mod must list "tic.loader" in ModDependencies, so TIC Loader starts first.
/// Register everything from StartEx: TIC Loader reads mods once all mods have
/// loaded, then applies the merged edits to the game's tables in memory.
/// </summary>
public interface ITICLoader
{
    /// <summary>Rises when members are added; existing members never change meaning.</summary>
    int ApiVersion { get; }

    /// <summary>
    /// Adds a reader for another mod format. When all mods have loaded, TIC Loader
    /// asks every format about every enabled mod, in load order; edits from later
    /// mods win field by field, exactly as for TIC/tables.json.
    /// </summary>
    void AddModFormat(ITICModFormat format);

    /// <summary>
    /// Adds edits from code, as if they came from mod <paramref name="sourceModId"/>
    /// (they take that mod's place in the load order). Throws FormatException for
    /// an unknown table, record, field or value.
    /// </summary>
    void AddEdits(string sourceModId, IEnumerable<TICFieldEdit> edits);

    /// <summary>Every table TIC Loader can edit, with each field's byte offset, for mapping other formats.</summary>
    IReadOnlyList<TICTableInfo> Tables { get; }

    /// <summary>The recognised game version, or null if unknown or not yet checked.</summary>
    string? GameVersion { get; }

    /// <summary>Raised once the edits are in the game's memory (on a background thread).</summary>
    event Action? TablesApplied;
}

/// <summary>A mod format TIC Loader can read, e.g. a bridge for another loader's data mods.</summary>
public interface ITICModFormat
{
    /// <summary>Shown in TIC Loader's log, e.g. "MyLoader XML tables".</summary>
    string Name { get; }

    /// <summary>
    /// The edits in a mod's folder, or null if the mod isn't in this format.
    /// For a mod in this format that is broken, throw FormatException with a
    /// clear message: TIC Loader logs it and skips just that mod.
    /// </summary>
    IReadOnlyList<TICFieldEdit>? Read(string modId, string modFolder);
}

/// <summary>
/// One field of one record. <see cref="Id"/> is the game's own record id (e.g.
/// armor starts at 0x90). <see cref="Value"/> uses TIC Patcher's text: a number,
/// or flag names joined by ", ". <see cref="Field"/> may also be "+N" for the
/// raw byte at offset N of the record (value 0-255), for bytes without a name.
/// </summary>
public readonly record struct TICFieldEdit(string Table, int Id, string Field, string Value);

/// <summary>A table TIC Loader can edit.</summary>
public sealed record TICTableInfo(string Name, int FirstId, int RecordCount, int RecordSize, IReadOnlyList<TICFieldInfo> Fields);

/// <summary>
/// A field: its byte offset and size in the record, its kind ("Byte", "UInt16",
/// "Flags", "Bits", "AbilityId"), and for flags the bit names, most significant
/// bit of the first byte first (null for unnamed bits).
/// </summary>
public sealed record TICFieldInfo(string Name, int Offset, int Size, string Kind, IReadOnlyList<string?>? Flags);
