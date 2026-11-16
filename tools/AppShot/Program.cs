using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TICPatcher.App;

// AppShot <memory image> <output folder> [tab:record ...]
// e.g. AppShot image.bin shots 0:1 4:240 renders the Jobs tab on Squire and the Items tab on Potion.
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var window = new MainWindow(["--from-image", args[0]]) { Width = 1200, Height = 800 };
window.Show();
Directory.CreateDirectory(args[1]);
foreach (var shot in args.Skip(2).DefaultIfEmpty("0:1"))
{
    var parts = shot.Split(':').Select(int.Parse).ToArray();
    var tabs = window.GetLogicalDescendants().OfType<TabControl>().First();
    tabs.SelectedIndex = parts[0];
    Pump();
    var list = ((Control)tabs.SelectedContent!).GetLogicalDescendants().OfType<ListBox>().First();
    list.SelectedIndex = parts[1];
    Pump();
    var path = Path.Combine(args[1], $"tab{parts[0]}-rec{parts[1]}.png");
    window.CaptureRenderedFrame()?.Save(path);
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
