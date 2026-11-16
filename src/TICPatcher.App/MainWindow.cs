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
/// read the original tables from the running game, and open/save of a mod's
/// TIC/tables.json.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly (string Tab, TableSchema[] Tables)[] Groups =
    [
        ("Jobs", [Jobs.Schema, SkillSets.JobRequirements]),
        ("Skill Sets", [SkillSets.Main, SkillSets.Extra, SkillSets.ActionMenus]),
        ("Monster Skills", [SkillSets.MonsterSkills]),
        ("Abilities", [Abilities.Common, Abilities.Action, Abilities.Throw, Abilities.Jump, Abilities.Charge, Abilities.Effects, Abilities.Animations]),
        ("Items", [Items.Common, Items.Weapons, Items.Shields, Items.Armor, Items.Accessories, Items.ChemistItems, Items.Extra, Items.ExtraWeapons]),
        ("Item Attributes", [Items.Attributes]),
        ("Shops", [Items.ShopInventories]),
        ("Statuses", [Statuses.Attributes, Statuses.Inflict]),
    ];

    private static readonly FilePickerFileType TablesJson = new("TIC mod tables") { Patterns = ["tables.json", "*.json"] };

    private readonly ContentControl _body = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
    private ModProject? _project;
    private string? _path;

    public MainWindow(string[] args)
    {
        Title = "TIC Patcher";
        Width = 1100;
        Height = 760;

        var read = new Button { Content = "Read from game" };
        var open = new Button { Content = "Open mod…" };
        var save = new Button { Content = "Save mod…" };
        read.Click += (_, _) => ReadFromGame();
        open.Click += async (_, _) => await OpenMod();
        save.Click += async (_, _) => await SaveMod();

        var toolbar = new DockPanel { Margin = new Thickness(8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(read);
        buttons.Children.Add(open);
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Left);
        toolbar.Children.Add(buttons);
        toolbar.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_body);
        Content = root;

        var image = Array.IndexOf(args, "--from-image") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        if (image is not null)
            Load(VanillaData.FromImage(File.ReadAllBytes(image)), $"Read {image}");
        else if (VanillaData.Saved().FirstOrDefault() is { } saved)
            Load(VanillaData.Load(saved), "Original tables loaded from this PC.");
        else
            ShowWelcome();
    }

    private void Status(string text) => _status.Text = text;

    private void ShowWelcome()
    {
        _body.Content = new TextBlock
        {
            Text = "Start FINAL FANTASY TACTICS - The Ivalice Chronicles, wait for the title screen, then click \"Read from game\".\n\n"
                + "TIC Patcher copies the game's original tables from your own running game (read-only), so no game data is shipped with it.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(24),
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
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
        var name = _path is null ? "new mod" : Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(_path))) ?? _path;
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
            Load(_project.Vanilla, $"Opened {path}", edits);
        }
        catch (Exception error) when (error is FormatException or IOException or System.Text.Json.JsonException)
        {
            Status($"Couldn't open {Path.GetFileName(path)}: {error.Message}");
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
