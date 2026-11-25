using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TICPatcher.App;

// AppShot <memory image> <output folder> [tab:record or tab:subtab:record ...]
// e.g. AppShot image.bin shots 0:1 4:240 3:1:32 renders Jobs on Squire, Items on Potion,
// and Abilities > ActionAbilities on Haste.
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

// A saved vanilla .json (which can include the battles) works too.
// An .exe is the installed game itself (maps, battles and text are read from its files).
var source = Path.GetExtension(args[0]).ToLowerInvariant() switch { ".json" => "--from-vanilla", ".exe" => "--from-game", _ => "--from-image" };
var window = new MainWindow([source, args[0]]) { Width = 1200, Height = 1000 };
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
    // mem:label prints the editor's memory: managed heap (after a full collection) and the process's.
    if (shot.StartsWith("mem:"))
    {
        Pump();
        var before = GC.GetTotalMemory(false);
        var managed = GC.GetTotalMemory(true);
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine($"mem {shot[4..]}: managed {before / 1048576} MB, {managed / 1048576} MB after collecting; private {self.PrivateMemorySize64 / 1048576} MB, working set {self.WorkingSet64 / 1048576} MB");
        continue;
    }
    // tab:N selects a top-level tab without taking a picture.
    if (shot.StartsWith("tab:"))
    {
        window.GetLogicalDescendants().OfType<TabControl>().First().SelectedIndex = int.Parse(shot[4..]);
        Pump();
        continue;
    }
    // click:Label presses the button with that text on the current tab.
    if (shot.StartsWith("click:"))
    {
        var current = (Control)window.GetLogicalDescendants().OfType<TabControl>().First().SelectedContent!;
        var button = current.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == shot[6..]);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        Console.WriteLine($"clicked {shot[6..]}");
        continue;
    }
    // bump:N:times raises the Nth number box on the current tab by one, several times, as clicking
    // its up arrow does (the value changes without the box taking the keyboard focus).
    if (shot.StartsWith("bump:"))
    {
        var e = shot.Split(':');
        var current = (Control)window.GetLogicalDescendants().OfType<TabControl>().First().SelectedContent!;
        var box = current.GetLogicalDescendants().OfType<NumericUpDown>().Where(n => n.IsEffectivelyVisible).ElementAt(int.Parse(e[1]));
        for (var i = 0; i < int.Parse(e[2]); i++)
        {
            box.Value += 1;
            Pump();
        }
        Console.WriteLine($"bumped {e[2]}x: box shows {box.Value}, still on screen: {box.IsAttachedToVisualTree()}");
        continue;
    }
    // type:N:text types into the Nth number box on the current tab, key by key, as a user would.
    if (shot.StartsWith("type:"))
    {
        var e = shot.Split(':');
        var current = (Control)window.GetLogicalDescendants().OfType<TabControl>().First().SelectedContent!;
        var box = current.GetLogicalDescendants().OfType<NumericUpDown>().Where(n => n.IsEffectivelyVisible).ElementAt(int.Parse(e[1]));
        box.Focus();
        Pump();
        box.GetVisualDescendants().OfType<TextBox>().First().SelectAll();
        foreach (var c in e[2])
        {
            window.KeyTextInput(c.ToString());
            Pump();
        }
        Console.WriteLine($"typed {e[2]}: box shows {box.Text}, value {box.Value}, still focused: {box.IsKeyboardFocusWithin}, attached: {box.IsAttachedToVisualTree()}");
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
    if (content.GetLogicalDescendants().OfType<ListBox>().FirstOrDefault() is { } list)
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
