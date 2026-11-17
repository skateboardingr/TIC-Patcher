using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.App;

/// <summary>
/// The editor window: FFTPatcher-style tabs over every table, a toolbar to
/// read the original tables from the running game, open/save of a mod's
/// TIC/tables.json, and Play: install the mod into Reloaded-II, turn it on
/// and launch the game.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly (string Tab, TableSchema[] Tables)[] Groups =
    [
        ("Jobs", [Jobs.Schema, SkillSets.JobRequirements]),
        ("Skill Sets", [SkillSets.Main, SkillSets.Extra, SkillSets.ActionMenus]),
        ("Monster Skills", [SkillSets.MonsterSkills]),
        ("Abilities", [Abilities.Common, Abilities.Action, Abilities.Throw, Abilities.Jump, Abilities.Charge, Abilities.Effects, Abilities.Animations]),
        ("Items", [Items.Common, Items.Weapons, Items.Shields, Items.Armor, Items.Accessories, Items.ChemistItems, Items.Extra, Items.ExtraWeapons, Items.ExtraArmor]),
        ("Item Attributes", [Items.Attributes]),
        ("Shops", [Items.ShopInventories]),
        ("Statuses", [Statuses.Attributes, Statuses.Inflict]),
    ];

    private static readonly FilePickerFileType TablesJson = new("TIC mod tables") { Patterns = ["tables.json", "*.json"] };

    private readonly ContentControl _body = new();
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(10, 6) };
    private ModProject? _project;
    private string? _path;
    private readonly TextBox _modName = new() { PlaceholderText = "Mod name", Width = 200 };

    public MainWindow(string[] args)
    {
        Title = "TIC Patcher";
        Width = 1100;
        Height = 760;

        var find = new Button { Content = "Find game…" };
        ToolTip.SetTip(find, "Pick FFT_enhanced.exe if TIC Patcher didn't find your game.");
        find.Click += async (_, _) => await FindGame();
        var open = new Button { Content = "Open mod…" };
        var save = new Button { Content = "Save mod…" };
        var play = new Button { Content = "▶ Play" };
        ToolTip.SetTip(play, "Install this mod into Reloaded-II, turn it on, and start the game with it.");
        play.Click += (_, _) => Play();
        _modName.TextChanged += (_, _) => UpdateTitle();
        open.Click += async (_, _) => await OpenMod();
        save.Click += async (_, _) => await SaveMod();

        var toolbar = new DockPanel { Margin = new Thickness(8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(find);
        buttons.Children.Add(open);
        buttons.Children.Add(save);
        buttons.Children.Add(new TextBlock { Text = "Mod:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        buttons.Children.Add(_modName);
        buttons.Children.Add(play);
        DockPanel.SetDock(buttons, Dock.Left);
        toolbar.Children.Add(buttons);

        var statusBar = new Border { Child = _status, BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Avalonia.Media.Brushes.Gray };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(_body);
        Content = root;

        _modName.Text = "My TIC mod";
        var image = Array.IndexOf(args, "--from-image") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        if (image is not null)
            Load(VanillaData.FromImage(File.ReadAllBytes(image)), $"Read {image}");
        else if (!TryLoadExecutable(GameLocator.Find()))
        {
            if (VanillaData.Saved().FirstOrDefault() is { } saved)
                Load(VanillaData.Load(saved), "Original values loaded from this PC's copy.");
            else
                ShowWelcome();
        }
    }

    private void Status(string text) => _status.Text = text;

    // Reads the original values from the installed game's executable (read-only).
    private bool TryLoadExecutable(string? exe)
    {
        if (exe is null)
            return false;
        try
        {
            var vanilla = VanillaData.FromExecutable(exe);
            vanilla.Save();
            Load(vanilla, "Original values read from your installed game.", _project?.Edits);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or InvalidOperationException)
        {
            Status($"Couldn't read {exe}: {error.Message}");
            return false;
        }
    }

    private async Task FindGame()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Find FFT_enhanced.exe (in the game's Steam folder)",
            FileTypeFilter = [new FilePickerFileType("The game") { Patterns = [GameLocator.ExeName] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            TryLoadExecutable(path);
    }

    private void ShowWelcome()
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock
        {
            Text = "TIC Patcher couldn't find FINAL FANTASY TACTICS - The Ivalice Chronicles.\n\n"
                + "Click \"Find game…\" and pick FFT_enhanced.exe in the game's Steam folder. TIC Patcher reads the original values "
                + "from your own copy of the game (read-only), so no game data is shipped with it.\n\n"
                + "If that doesn't work, start the game from Steam, wait for the title screen, and read the values from the running game instead.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var read = new Button { Content = "Read from the running game" };
        read.Click += (_, _) => ReadFromGame();
        panel.Children.Add(read);
        _body.Content = panel;
    }

    private void Load(VanillaData vanilla, string message, TableEdits? edits = null)
    {
        _project = new ModProject(vanilla, edits);
        _project.Changed += UpdateTitle;
        var tabs = new TabControl();
        foreach (var (tab, tables) in Groups)
        {
            var available = tables.Where(t => _project.HasTable(t.Name)).ToArray();
            if (available.Length == 0)
                continue;
            object content = available.Length == 1
                ? new TableEditor(_project, available[0], Status)
                : new TabControl
                {
                    ItemsSource = available.Select(t => new TabItem { Header = t.Name, Content = new TableEditor(_project, t, Status) }).ToList(),
                };
            tabs.Items.Add(new TabItem { Header = tab, Content = content });
        }
        _body.Content = tabs;
        Status($"{message} ({vanilla.BuildName})");
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var count = _project?.Edits.All.Count() ?? 0;
        var name = string.IsNullOrWhiteSpace(_modName.Text) ? "new mod" : _modName.Text.Trim();
        Title = $"TIC Patcher - {name}{(count > 0 ? $" ({count} change{(count == 1 ? "" : "s")})" : "")}";
    }

    private void ReadFromGame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Status("Reading from the game works on Windows for now.");
            return;
        }
        var game = Process.GetProcessesByName("FFT_enhanced").FirstOrDefault();
        if (game is null)
        {
            Status("The game isn't running. Start it, wait for the title screen, and try again.");
            return;
        }
        // The original tables must come from an unmodded game: with Reloaded-II
        // (and so TIC Loader) inside, the copy would hold the mods' values.
        if (RunsReloaded(game))
        {
            Status("The game was started through Reloaded-II, so its tables may be modded. Close it, start it from Steam, then Read from game.");
            return;
        }
        try
        {
            var vanilla = VanillaData.FromProcess(game);
            vanilla.Save();
            Load(vanilla, "Read the original tables from the game.", _project?.Edits);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            Status(error.Message);
        }
    }

    private static bool RunsReloaded(Process game)
    {
        try
        {
            return game.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.StartsWith("Reloaded", StringComparison.OrdinalIgnoreCase));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // can't list modules; FromProcess reports any real problem
        }
    }

        private async Task OpenMod()
    {
        if (_project is null)
        {
            Status("Read the original tables from the game first.");
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a TIC mod's tables.json",
            FileTypeFilter = [TablesJson],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return;
        try
        {
            var edits = TableEdits.Parse(await File.ReadAllTextAsync(path));
            _path = path;
            // A mod opened from its folder (…/<mod>/TIC/tables.json) keeps its name.
            var config = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? "", "ModConfig.json");
            if (File.Exists(config) && System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(config))?["ModName"] is { } name)
                _modName.Text = (string?)name;
            Load(_project.Vanilla, $"Opened {path}", edits);
        }
        catch (Exception error) when (error is FormatException or IOException or System.Text.Json.JsonException)
        {
            Status($"Couldn't open {Path.GetFileName(path)}: {error.Message}");
        }
    }

    private void Play()
    {
        if (_project is null)
        {
            Status("Read the original tables from the game first.");
            return;
        }
        if (Process.GetProcessesByName("FFT_enhanced").Length > 0)
        {
            Status("Close the game first: mods are applied when it starts.");
            return;
        }
        if (Process.GetProcessesByName("Reloaded-II").Length > 0)
        {
            Status("Close Reloaded-II's window first, so it doesn't undo the change.");
            return;
        }
        if (ReloadedSetup.Find(out var problem) is not { } reloaded)
        {
            Status(problem);
            return;
        }
        if (!reloaded.LoaderInstalled)
        {
            Status("TIC Loader isn't in Reloaded-II's Mods folder yet; install it first.");
            return;
        }
        var name = string.IsNullOrWhiteSpace(_modName.Text) ? "My TIC mod" : _modName.Text.Trim();
        var id = ReloadedSetup.IdFor(name);
        try
        {
            var folder = reloaded.Install(id, name, Environment.UserName, "Made with TIC Patcher.", _project.Edits.ToJson());
            if (!reloaded.Enable(id))
            {
                Status("Add the game to Reloaded-II first (Add an Application, pick FFT_enhanced.exe).");
                return;
            }
            Process.Start(reloaded.LaunchCommand());
            _path = Path.Combine(folder, ModCollector.TablesFile);
            UpdateTitle();
            Status($"Started the game with \"{name}\" ({_project.Edits.All.Count()} change(s)).");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status("Couldn't start: " + error.Message);
        }
    }

    private async Task SaveMod()
    {
        if (_project is null)
            return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the mod's tables.json (in the mod's TIC folder)",
            SuggestedFileName = "tables.json",
            DefaultExtension = "json",
            FileTypeChoices = [TablesJson],
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;
        await File.WriteAllTextAsync(path + ".tmp", _project.Edits.ToJson());
        File.Move(path + ".tmp", path, overwrite: true);
        _path = path;
        UpdateTitle();
        Status($"Saved {_project.Edits.All.Count()} change(s) to {path}");
    }
}
