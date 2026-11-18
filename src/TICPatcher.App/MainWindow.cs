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
    private ModsView? _mods;
    private string? _modId; // the Reloaded-II mod being edited; null for a new mod
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
        var open = new Button { Content = "Import…" };
        ToolTip.SetTip(open, "Open a tables.json, e.g. one someone shared. Save mod then adds it to your mods.");
        var newMod = new Button { Content = "New" };
        ToolTip.SetTip(newMod, "Start a new mod from the game's original values.");
        newMod.Click += (_, _) => NewMod();
        var save = new Button { Content = "Save mod" };
        ToolTip.SetTip(save, "Save this mod into Reloaded-II. It appears in the Mods tab.");
        var export = new Button { Content = "Export…" };
        ToolTip.SetTip(export, "Save the changes as a tables.json file, e.g. to share.");
        export.Click += async (_, _) => await ExportMod();
        var play = new Button { Content = "▶ Play" };
        ToolTip.SetTip(play, "Install this mod into Reloaded-II, turn it on, and start the game with it.");
        play.Click += (_, _) => Play();
        _modName.TextChanged += (_, _) =>
        {
            UpdateTitle();
            SaveSession();
        };
        open.Click += async (_, _) => await OpenMod();
        save.Click += (_, _) => SaveMod();

        var toolbar = new DockPanel { Margin = new Thickness(8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(find);
        buttons.Children.Add(open);
        buttons.Children.Insert(0, newMod);
        buttons.Children.Add(save);
        buttons.Children.Add(export);
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
        else
        {
            if (!TryLoadExecutable(GameLocator.Find()))
            {
                if (VanillaData.Saved().FirstOrDefault() is { } saved)
                    Load(VanillaData.Load(saved), "Original values loaded from this PC's copy.");
                else
                    ShowWelcome();
            }
            RestoreSession();
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
        _project.Changed += SaveSession;
        var tabs = new TabControl();
        _mods = new ModsView(Status, mod => _ = OpenModFile(Path.Combine(mod.Folder, ModCollector.TablesFile)));
        tabs.Items.Add(new TabItem { Header = "Mods", Content = _mods });
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
        tabs.SelectedIndex = 1; // straight to editing; Mods is one click away
        _body.Content = tabs;
        SaveSession();
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
            Title = "Import a TIC mod's tables.json",
            FileTypeFilter = [TablesJson],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await OpenModFile(path);
    }

    private async Task OpenModFile(string path)
    {
        if (_project is null)
            return;
        try
        {
            var edits = TableEdits.Parse(await File.ReadAllTextAsync(path));
            _path = path;
            // A mod opened from its folder (…/<mod>/TIC/tables.json) keeps its name and id,
            // so saving updates that mod rather than making a new one.
            _modId = null;
            var config = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? "", "ModConfig.json");
            if (File.Exists(config) && System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(config)) is { } mod)
            {
                _modId = (string?)mod["ModId"];
                if ((string?)mod["ModName"] is { } name)
                    _modName.Text = name;
            }
            else
            {
                // A loose file becomes a new mod, named after the file unless it's just "tables.json".
                var file = Path.GetFileNameWithoutExtension(path);
                _modName.Text = file.Equals("tables", StringComparison.OrdinalIgnoreCase) ? "Imported mod" : file;
            }
            Load(_project.Vanilla, _modId is null
                ? $"Imported {path}. Name it above and press Save mod to add it to your mods."
                : $"Opened {_modName.Text}.", edits);
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
        string? loaderNews;
        try
        {
            loaderNews = LoaderBundle.EnsureInstalled(reloaded);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status("Couldn't install TIC Loader into Reloaded-II: " + error.Message);
            return;
        }
        var name = ModName;
        try
        {
            var id = InstallCurrent(reloaded);
            if (!reloaded.Enable(id))
            {
                Status("Add the game to Reloaded-II first (Add an Application, pick FFT_enhanced.exe).");
                return;
            }
            Process.Start(reloaded.LaunchCommand());
            UpdateTitle();
            Status($"{(loaderNews is null ? "" : loaderNews + " ")}Started the game with \"{name}\" ({_project.Edits.All.Count()} change(s)).");
            _mods?.Fill();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status("Couldn't start: " + error.Message);
        }
    }

    // Remembers the mod being edited, as it's edited, so closing the editor never loses work.
    private void SaveSession()
    {
        if (_restoring || _project is null)
            return;
        try
        {
            new EditorSession(_modId, ModName, _project.Edits.ToJson()).Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status("Couldn't remember this session: " + error.Message);
        }
    }

    private bool _restoring;

    private void RestoreSession()
    {
        if (_project is null || EditorSession.Load() is not { } session)
            return;
        TableEdits edits;
        try
        {
            edits = TableEdits.Parse(session.TablesJson);
        }
        catch (Exception error) when (error is FormatException or System.Text.Json.JsonException)
        {
            Status("Couldn't restore your last session: " + error.Message);
            return;
        }
        _restoring = true;
        _modId = session.ModId;
        _modName.Text = session.ModName;
        _restoring = false;
        var count = edits.All.Count();
        Load(_project.Vanilla, count == 0 ? $"Ready: {session.ModName}." : $"Picked up where you left off: {session.ModName} ({count} change(s)).", edits);
    }

    private string ModName => string.IsNullOrWhiteSpace(_modName.Text) ? "My TIC mod" : _modName.Text.Trim();

    // Writes the mod into Reloaded-II's Mods folder: the mod being edited, or a new one named after the mod name.
    private string InstallCurrent(ReloadedSetup reloaded)
    {
        var id = _modId ?? ReloadedSetup.IdFor(ModName);
        var folder = reloaded.Install(id, ModName, Environment.UserName, "Made with TIC Patcher.", _project!.Edits.ToJson());
        _modId = id;
        _path = Path.Combine(folder, ModCollector.TablesFile);
        _mods?.Fill();
        SaveSession();
        return id;
    }

    private void NewMod()
    {
        if (_project is null)
            return;
        _modId = null;
        _path = null;
        _modName.Text = "My TIC mod";
        Load(_project.Vanilla, "New mod: every value is the game's original.");
    }

    private void SaveMod()
    {
        if (_project is null)
            return;
        if (ReloadedSetup.Find(out var problem) is not { } reloaded)
        {
            Status(problem + " Use Export… to save the changes as a file instead.");
            return;
        }
        try
        {
            InstallCurrent(reloaded);
            UpdateTitle();
            Status($"Saved \"{ModName}\" ({_project.Edits.All.Count()} change(s)). It's in the Mods tab; tick it there, or press Play.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status("Couldn't save: " + error.Message);
        }
    }

    private async Task ExportMod()
    {
        if (_project is null)
            return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the mod's changes as tables.json",
            SuggestedFileName = "tables.json",
            DefaultExtension = "json",
            FileTypeChoices = [TablesJson],
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;
        await File.WriteAllTextAsync(path + ".tmp", _project.Edits.ToJson());
        File.Move(path + ".tmp", path, overwrite: true);
        Status($"Exported {_project.Edits.All.Count()} change(s) to {path}");
    }
}
