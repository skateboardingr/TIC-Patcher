using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;
using TICPatcher.Core.Import;
using TICPatcher.Core.Text;

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
        ("Jobs", [Jobs.Schema, SkillSets.JobRequirements, Jobs.FormationSprites1, Jobs.FormationSprites2, Jobs.FormationSlotSprites, Jobs.SlotSprites, Jobs.SpriteFormationSlots]),
        ("Skill Sets", [SkillSets.Main, SkillSets.Extra, SkillSets.ActionMenus]),
        ("Monster Skills", [SkillSets.MonsterSkills]),
        ("Abilities", [Abilities.Common, Abilities.Action, Abilities.Throw, Abilities.Jump, Abilities.Charge, Abilities.Effects, Abilities.Animations]),
        ("Items", [Items.Common, Items.Weapons, Items.Shields, Items.Armor, Items.Accessories, Items.ChemistItems, Items.Extra, Items.ExtraWeapons, Items.ExtraArmor]),
        ("Item Attributes", [Items.Attributes]),
        ("Shops", [Items.ShopInventories]),
        ("Statuses", [Statuses.Attributes, Statuses.Inflict]),
        ("Errands", [Errands.Schema, Errands.Eligibility]),
    ];

    private static readonly FilePickerFileType TablesJson = new("TIC mod tables") { Patterns = ["tables.json", "*.json"] };
    private static readonly FilePickerFileType FftPatchFile = new("FFTPatcher patch") { Patterns = ["*.fftpatch"] };
    private static readonly FilePickerFileType Importable = new("TIC mods and FFTPatcher patches") { Patterns = ["*.json", "*.fftpatch"] };

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
        ToolTip.SetTip(play, "Start the game with the mods ticked in the Mods tab. Doesn't save anything.");
        var inject = new Button { Content = "Inject" };
        ToolTip.SetTip(inject, "Put the editor's current values into the running game, to try them without saving. Temporary until the game closes.");
        inject.Click += (_, _) => Inject();
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
        buttons.Children.Add(inject);
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
        // Lists show the game's own names, with this mod's renames.
        Names.Provider = (list, id) =>
            _project is { GameText: not null } project && TextTables.ForNames(list) is { } text ? project.CurrentText(text, id, "Name") : null;
        var image = Array.IndexOf(args, "--from-image") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var vanillaFile = Array.IndexOf(args, "--from-vanilla") is var v and >= 0 && v + 1 < args.Length ? args[v + 1] : null;
        var gameFile = Array.IndexOf(args, "--from-game") is var g and >= 0 && g + 1 < args.Length ? args[g + 1] : null;
        // Started for a test or a screenshot: the user's saved session is left alone.
        _sessionless = image is not null || vanillaFile is not null || gameFile is not null;
        if (gameFile is not null)
            TryLoadExecutable(gameFile);
        else if (image is not null)
            Load(VanillaData.FromImage(File.ReadAllBytes(image)), $"Read {image}");
        else if (vanillaFile is not null)
            Load(VanillaData.Load(vanillaFile), $"Read {vanillaFile}");
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
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    vanilla.AddBattles(exe);
                    vanilla.AddSpritePalettes(exe);
                }
                catch (Exception error) when (error is IOException or FormatException or InvalidOperationException
                    or System.Runtime.InteropServices.COMException or DllNotFoundException or EntryPointNotFoundException)
                {
                    Status($"Couldn't read the battles: {error.Message}");
                }
            }
            vanilla.Save();
            ReadGameText(exe);
            _gameExe = exe;
            Load(vanilla, "Original values read from your installed game.", _project?.Edits);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or InvalidOperationException)
        {
            Status($"Couldn't read {exe}: {error.Message}");
            return false;
        }
    }

    // The game's names and descriptions, from the cache or else the game's archives.
    private GameText? _gameText;

    // Pictures next to fields that pick a sprite (jobs here; battle units in BattleView).
    private Func<int, Control?>? PreviewFor(TableSchema table)
    {
        if (table != Jobs.Schema || _project is null || Maps() is not { } maps)
            return null;
        var pictures = new SpritePictures(_project, maps);
        return job =>
        {
            var portrait = int.Parse(_project.Current(Jobs.Schema, job, "MPortrait"));
            var palette = int.Parse(_project.Current(Jobs.Schema, job, "MPalette"));
            // Monsters: their own sprite and palette; human jobs: the male and female sprites.
            var sprites = portrait > 0
                ? new[] { (Core.Sprites.UnitSprite.Of(0x82, false, job, 0, portrait, palette), "") }
                : new[]
                {
                    (Core.Sprites.UnitSprite.Of(0x80, false, job, 0, 0, 0), "male"),
                    (Core.Sprites.UnitSprite.Of(0x81, true, job, 0, 0, 0), "female"),
                };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (sprite, label) in sprites.Where(s => s.Item1 is not null).DistinctBy(s => s.Item1))
            {
                var (id, pal) = sprite!.Value;
                var one = new StackPanel { Spacing = 2 };
                one.Children.Add(SpritePictures.Show(pictures.Frame(id, pal), 3));
                one.Children.Add(new TextBlock { Text = label.Length > 0 ? $"{label}: sprite {id:X2}" : SpritePictures.Caption(id, pal), Opacity = 0.75, FontSize = 12 });
                panel.Children.Add(one);
            }
            return panel.Children.Count > 0 ? panel : null;
        };
    }

    // The installed game, once found: battle maps are read from its archives.
    private string? _gameExe;
    private Core.Maps.MapSource? _maps;

    private Core.Maps.MapSource? Maps()
    {
        if (!OperatingSystem.IsWindows() || (_gameExe ??= GameLocator.Find()) is not { } exe)
            return null;
        try
        {
            return _maps ??= new Core.Maps.MapSource(exe, _gameText);
        }
        catch (Exception error) when (error is IOException or FormatException or UnauthorizedAccessException)
        {
            Status($"Couldn't open the game's maps: {error.Message}");
            return null;
        }
    }

    private void ReadGameText(string exe)
    {
        // A cache from an older TIC Patcher may lack tables mapped since; read it again then.
        _gameText ??= GameText.Load(GameText.CachePath("en"));
        if (_gameText is not null && TextTables.List.All(t => _gameText.Tables.ContainsKey(t.Name)) && _gameText.MapPlaces.Count > 0
            || !OperatingSystem.IsWindows())
            return;
        try
        {
            _gameText = GameText.FromGame(exe);
            _gameText.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            Status($"Couldn't read the game's text (names and descriptions): {error.Message}");
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
        _project = new ModProject(vanilla, edits) { GameText = _gameText ??= GameText.Load(GameText.CachePath("en")) };
        _project.Changed += UpdateTitle;
        _project.Changed += SaveSession;
        var tabs = new TabControl();
        _mods = new ModsView(Status, mod => _ = OpenModFile(Path.Combine(mod.Folder, ModCollector.TablesFile)), mod => _ = MergeMod(mod));
        tabs.Items.Add(new TabItem { Header = "Mods", Content = _mods });
        _places.Clear();
        foreach (var (tab, tables) in Groups)
        {
            var available = tables.Where(t => _project.HasTable(t.Name)).ToArray();
            if (available.Length == 0)
                continue;
            var editors = available.Select(t => new TableEditor(_project, t, Status, Navigate) { Preview = PreviewFor(t) }).ToArray();
            var top = new TabItem { Header = tab };
            if (tab == "Shops" && editors.Length == 1)
            {
                // Shops can be edited by town as well as by item.
                var sub = new TabControl
                {
                    ItemsSource = new List<TabItem>
                    {
                        new() { Header = "By shop", Content = new ShopView(_project, Status) },
                        new() { Header = "By item", Content = editors[0] },
                    },
                };
                top.Content = sub;
                _places[available[0].Name] = (top, sub, 1, editors[0]);
            }
            else if (editors.Length == 1)
            {
                top.Content = editors[0];
                _places[available[0].Name] = (top, null, 0, editors[0]);
            }
            else
            {
                var sub = new TabControl { ItemsSource = available.Select((t, i) => new TabItem { Header = t.Name, Content = editors[i] }).ToList() };
                top.Content = sub;
                for (var i = 0; i < available.Length; i++)
                    _places[available[i].Name] = (top, sub, i, editors[i]);
            }
            tabs.Items.Add(top);
        }
        // Battles: pick a battle, then edit its units.
        if (_project.HasTable(Battles.Units.Name))
        {
            var battles = new BattleView(_project, Status, Navigate, Maps());
            var top = new TabItem { Header = "Battles", Content = battles };
            tabs.Items.Add(top);
            _places[Battles.Units.Name] = (top, null, 0, battles.Units);
        }
        // Sprites: palettes to view and recolour.
        if (_project.HasTable(Core.Tables.Schemas.SpriteSheets.Palettes.Name) && Maps() is { } spriteMaps)
            tabs.Items.Add(new TabItem { Header = "Sprites", Content = new SpritesView(_project, new SpritePictures(_project, spriteMaps), Status) });
        // Unit names have no stat table of their own.
        if (_project.GameText?.Tables.ContainsKey(TextTables.Characters.Name) == true)
            tabs.Items.Add(new TabItem { Header = "Characters", Content = new TextView(_project, TextTables.Characters, Status) });
        _tabs = tabs;
        tabs.SelectedIndex = 1; // straight to editing; Mods is one click away
        _body.Content = tabs;
        SaveSession();
        Status($"{message} ({vanilla.BuildName})");
        UpdateTitle();
    }

    // Where each table's editor sits, so a field like an item's Item Attributes can jump to its entry.
    private readonly Dictionary<string, (TabItem Top, TabControl? Sub, int Index, TableEditor Editor)> _places = new(StringComparer.Ordinal);
    private TabControl? _tabs;

    private void Navigate(string table, int id)
    {
        // The id may live in a sibling table, e.g. skill set 0xB0 is a monster skill set.
        if (_tabs is null || AllTables.Resolve(table, id) is not { } holder || !_places.TryGetValue(holder.Name, out var place))
        {
            Status($"Nothing to show for {table} {id:X2}.");
            return;
        }
        _tabs.SelectedItem = place.Top;
        if (place.Sub is not null)
            place.Sub.SelectedIndex = place.Index;
        place.Editor.Select(id);
    }

    private void UpdateTitle()
    {
        var count = _project?.Edits.Count ?? 0;
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
            Title = "Import a TIC mod's tables.json or an FFTPatcher patch",
            FileTypeFilter = [Importable, TablesJson, FftPatchFile],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return;
        if (path.EndsWith(".fftpatch", StringComparison.OrdinalIgnoreCase))
            await ImportFftPatch(path);
        else
            await OpenModFile(path);
    }

    // Where FFTPatcher is, remembered: its defaults show what a patch changed.
    private static string FftPatcherSetting => Path.Combine(VanillaData.Folder, "fftpatcher-folder.txt");

    private async Task<string?> FftPatcherFolder()
    {
        var saved = File.Exists(FftPatcherSetting) ? File.ReadAllText(FftPatcherSetting).Trim() : null;
        if (saved is not null && File.Exists(Path.Combine(saved, "PatcherLib.Resources.dll")))
            return saved;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick the folder FFTPatcher.exe is in (its default tables show what the patch changed)",
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
            return null;
        Directory.CreateDirectory(VanillaData.Folder);
        File.WriteAllText(FftPatcherSetting, folder);
        return folder;
    }

    private async Task ImportFftPatch(string path)
    {
        if (_project is null || await FftPatcherFolder() is not { } folder)
            return;
        try
        {
            var patch = FftPatch.Read(path);
            var defaults = FftPatch.ReadDefaults(folder);
            // A new mod holding just the patch's changes, on top of TIC's own values.
            var imported = new ModProject(_project.Vanilla) { GameText = _gameText };
            var report = patch.ApplyTo(imported, defaults);
            _path = null;
            _modId = null;
            _modName.Text = Path.GetFileNameWithoutExtension(path);
            Load(_project.Vanilla, $"{Path.GetFileName(path)} ({(patch.Psp ? "PSP" : "PSX")}): {report} Name it above and press Save mod.", imported.Edits);
        }
        catch (Exception error) when (error is FormatException or IOException or InvalidDataException or BadImageFormatException
            or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (error is FileNotFoundException or BadImageFormatException)
                File.Delete(FftPatcherSetting); // ask for the folder again next time
            Status($"Couldn't import {Path.GetFileName(path)}: {error.Message}");
        }
    }

    // Adds another mod's changes to the editor's, as a new combined mod (Save mod keeps it).
    private async Task MergeMod(ReloadedSetup.InstalledMod mod)
    {
        if (_project is null)
            return;
        try
        {
            var other = TableEdits.Parse(await File.ReadAllTextAsync(Path.Combine(mod.Folder, ModCollector.TablesFile)));
            var conflicts = _project.Edits.Conflicts(other);
            var merged = TableEdits.Parse(_project.Edits.ToJson());
            merged.Merge(other);
            var name = $"{ModName} + {mod.Name}";
            _modName.Text = name;
            _modId = null; // a new mod; the originals stay as they are
            _path = null;
            Load(_project.Vanilla, conflicts.Count == 0
                ? $"Merged {mod.Name}: {other.Count} change(s) added, no clashes. Press Save mod to keep \"{name}\"."
                : $"Merged {mod.Name}: {other.Count} change(s); {conflicts.Count} field(s) both mods set differently now have {mod.Name}'s value ({string.Join(", ", conflicts.Take(5))}{(conflicts.Count > 5 ? ", …" : "")}).",
                merged);
        }
        catch (Exception error) when (error is FormatException or IOException or System.Text.Json.JsonException)
        {
            Status($"Couldn't merge {mod.Name}: {error.Message}");
        }
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
        // Play just plays: the mods ticked in the Mods tab, nothing saved.
        try
        {
            if (reloaded.GameAppConfig() is null)
            {
                Status("Add the game to Reloaded-II first (Add an Application, pick FFT_enhanced.exe).");
                return;
            }
            Process.Start(reloaded.LaunchCommand());
            var mods = reloaded.EnabledMods().Count;
            var unsaved = Unsaved ? " Unsaved changes in the editor aren't included: Save mod first, or use Inject once the game is running." : "";
            Status($"{(loaderNews is null ? "" : loaderNews + " ")}Started the game with {mods} mod(s) ticked.{unsaved}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status("Couldn't start: " + error.Message);
        }
    }

    // Remembers the mod being edited, as it's edited, so closing the editor never loses work.
    private readonly bool _sessionless;

    private void SaveSession()
    {
        if (_restoring || _sessionless || _project is null)
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
        var count = edits.Count;
        Load(_project.Vanilla, count == 0 ? $"Ready: {session.ModName}." : $"Picked up where you left off: {session.ModName} ({count} change(s)).", edits);
    }

    // Whether the editor holds changes the mod's saved tables.json doesn't.
    private bool Unsaved =>
        _project is { } project && project.Edits.Count > 0
        && !(_path is not null && File.Exists(_path) && File.ReadAllText(_path) == project.Edits.ToJson());

    // Puts the editor's current values into the running game (temporary).
    private void Inject()
    {
        if (_project is null)
            return;
        if (!OperatingSystem.IsWindows())
        {
            Status("Inject works on Windows for now.");
            return;
        }
        var game = Process.GetProcessesByName("FFT_enhanced").FirstOrDefault();
        if (game is null)
        {
            Status("Start the game first (Play), then Inject puts the editor's values into it.");
            return;
        }
        Status("Injecting…");
        var project = _project;
        _ = Task.Run(() =>
        {
            string result;
            try
            {
                result = LiveInjector.Inject(game, project, _ => { });
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException or IOException or System.ComponentModel.Win32Exception)
            {
                result = "Couldn't inject: " + error.Message;
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Status(result));
        });
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
            Status($"Saved \"{ModName}\" ({_project.Edits.Count} change(s)). It's in the Mods tab; tick it there, or press Play.");
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
        Status($"Exported {_project.Edits.Count} change(s) to {path}");
    }
}
