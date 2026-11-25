using System.Diagnostics;
using System.Runtime.Versioning;
using TICPatcher.Core.Memory;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Mods;

/// <summary>
/// Puts the editor's current values into the running game, for trying changes
/// without saving a mod or restarting. Temporary: gone when the game closes.
/// Stat tables are written as the original values with the edits applied, so a
/// change that was reverted in the editor goes back to the original in the game too.
/// </summary>
[SupportedOSPlatform("windows")]
public static class LiveInjector
{
    public static string Inject(Process game, ModProject project, Action<string> log)
    {
        var module = game.MainModule ?? throw new InvalidOperationException("The game's executable isn't visible.");
        var start = (long)module.BaseAddress;
        var handle = ProcessMemory.Native.OpenProcess(ProcessMemory.Native.PROCESS_QUERY_INFORMATION | ProcessMemory.Native.PROCESS_VM_READ
            | ProcessMemory.Native.PROCESS_VM_WRITE | ProcessMemory.Native.PROCESS_VM_OPERATION, false, game.Id);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Couldn't open the game.");
        try
        {
            var memory = new ProcessMemory(handle);
            var build = GameBuilds.Identify(memory.Read(start, 0x1000))
                ?? throw new NotSupportedException("This version of the game isn't supported.");
            if (JobTableFingerprint.Find(memory.Read(start + build.Tables[Jobs.Schema.Name], Jobs.Schema.Length)) is not [0])
                throw new InvalidOperationException("The game's tables aren't loaded yet; wait for the title screen.");

            var bytes = 0;
            foreach (var (name, original) in project.Vanilla.Tables)
            {
                if (!build.Tables.ContainsKey(name) || !AllTables.ByName.TryGetValue(name, out var schema))
                    continue;
                var wanted = (byte[])original.Clone();
                project.Edits.ApplyTo(name, wanted, schema.RecordSize);
                foreach (var at in build.OffsetsOf(name))
                    bytes += memory.WriteChanges(start + at, memory.Read(start + at, wanted.Length), wanted);
            }

            // Battles live in a block of the game's own memory, found by the original battles.
            // Done even with no battle edits, so ones reverted in the editor go back too.
            var battles = "";
            if (project.Vanilla.Tables.TryGetValue(Battles.Units.Name, out var originalBattles))
            {
                var edited = project.Edits.ChangedTables.Contains(Battles.Units.Name);
                battles = BattleMemory.Find(memory, originalBattles) is { } at
                    ? $", {BattleMemory.Apply(memory, at, BattleMemory.Wanted(originalBattles, project.Edits))} byte(s) of battles"
                    : edited ? ", but the battles weren't found in the game's memory (not changed)" : "";
            }

            var texts = 0;
            if (project.Edits.AllText.Any())
                texts = Text.TextApplier.Relocate(memory, project.Edits, new Text.TextApplier.Done(), log);
            return $"Injected into the running game: {bytes} byte(s) of stats changed{battles}{(texts > 0 ? $", {texts} text table(s) updated" : "")}. Temporary until the game closes.";
        }
        finally
        {
            ProcessMemory.Native.CloseHandle(handle);
        }
    }
}
