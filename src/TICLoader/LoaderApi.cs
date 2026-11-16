using Reloaded.Mod.Interfaces;
using TICLoader.Interfaces;
using TICPatcher.Core.Mods;

namespace TICPatcher.Loader;

/// <summary>TIC Loader's public API, shared with other mods as a Reloaded-II controller.</summary>
internal sealed class LoaderApi(ModCollector collector) : ITICLoader
{
    public int ApiVersion => 1;

    public void AddModFormat(ITICModFormat format) => collector.AddFormat(format);

    public void AddEdits(string sourceModId, IEnumerable<TICFieldEdit> edits) => collector.AddEdits(sourceModId, edits);

    public IReadOnlyList<TICTableInfo> Tables { get; } = ModCollector.DescribeTables();

    public string? GameVersion { get; set; }

    public event Action? TablesApplied;

    public void RaiseTablesApplied(Action<string> log)
    {
        // Another mod's handler failing must not take TIC Loader down with it.
        foreach (var handler in TablesApplied?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                handler();
            }
            catch (Exception error)
            {
                log($"a TablesApplied handler failed: {error.Message}");
            }
        }
    }
}

/// <summary>
/// The types TIC Loader shares with other mods. Reloaded-II loads them once,
/// so a bridge's ITICModFormat and TIC Loader's ITICLoader are the same types.
/// </summary>
public sealed class Exports : IExports
{
    public Type[] GetTypes() =>
        [typeof(ITICLoader), typeof(ITICModFormat), typeof(TICFieldEdit), typeof(TICTableInfo), typeof(TICFieldInfo)];
}
