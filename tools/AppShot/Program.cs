using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TICPatcher.App;

// AppShot <memory image> <output folder> [tab:record or tab:subtab:record ...]
// e.g. AppShot image.bin shots 0:1 4:240 3:1:32 renders Jobs on Squire, Items on Potion,
// and Abilities > ActionAbilities on Haste.
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var window = new MainWindow(["--from-image", args[0]]) { Width = 1200, Height = 1000 };
window.Show();
Directory.CreateDirectory(args[1]);
var shotNumber = 0;
foreach (var shot in args.Skip(2).DefaultIfEmpty("0:1"))
{
    // edit:Table:id:Field:value changes the mod as if by hand, to check that other tabs follow.
    if (shot.StartsWith("edit:"))
    {
        var e = shot.Split(':');
        var project = (TICPatcher.Core.Mods.ModProject)typeof(MainWindow)
            .GetField("_project", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        project.Set(TICPatcher.Core.Tables.Schemas.AllTables.ByName[e[1]], Convert.ToInt32(e[2], 16), e[3], e[4]);
        Pump();
        continue;
    }
    var parts = shot.Split(':').Select(int.Parse).ToArray();
    var tabs = window.GetLogicalDescendants().OfType<TabControl>().First();
    tabs.SelectedIndex = parts[0];
    Pump();
    var content = (Control)tabs.SelectedContent!;
    if (parts.Length == 3 && content is TabControl subTabs)
    {
        subTabs.SelectedIndex = parts[1];
        Pump();
        content = (Control)subTabs.SelectedContent!;
    }
    var list = content.GetLogicalDescendants().OfType<ListBox>().First();
    list.SelectedIndex = parts[^1];
    Pump();
    var path = Path.Combine(args[1], $"shot{++shotNumber}-{string.Join('-', parts)}.png");
#pragma warning disable CS0618 // the replacement overload needs encoder plumbing this tool doesn't need
    window.CaptureRenderedFrame()?.Save(path);
#pragma warning restore CS0618
    Console.WriteLine(path);
}

static void Pump()
{
    for (var i = 0; i < 5; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
