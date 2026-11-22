using Avalonia;
using Avalonia.Controls;
using TICPatcher.Core.Maps;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.App;

/// <summary>
/// Battles (ENTD), as FFTPatcher shows them: pick a battle, then edit its 16
/// unit slots in an ordinary table editor. Above them, the battle's map from above,
/// with every unit on its tile; click a tile to move the selected unit there.
/// </summary>
public sealed class BattleView : UserControl
{
    private readonly ModProject _project;
    private readonly ListBox _battles = new();
    private readonly TextBox _filter = new() { PlaceholderText = "Filter battles", Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _changedOnly = new() { Content = "Changed only", Margin = new Thickness(0, 0, 0, 6) };
    private readonly List<int> _shown = [];
    private bool _stale;

    // The map panel; without the game's files (no MapSource) it explains that instead.
    private readonly MapSource? _maps;
    private readonly ComboBox _mapPicker = new() { MinWidth = 260, MaxDropDownHeight = 400 };
    private readonly CheckBox _upper = new() { Content = "Upper level", Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _mapNote = new() { Opacity = 0.7, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly MapGrid _grid;
    private readonly TextBlock _away = new() { Opacity = 0.7, Margin = new Thickness(8, 0, 8, 4) };
    private readonly Dictionary<int, int> _choices = BattleMaps.LoadChoices();
    private bool _picking;
    private readonly Action<string> _status;

    public TableEditor Units { get; }

    public BattleView(ModProject project, Action<string> status, Action<string, int> navigate, MapSource? maps = null)
    {
        _project = project;
        _status = status;
        _maps = maps;
        _grid = new MapGrid { TerrainName = surface => maps?.TerrainName(surface) };
        Units = new TableEditor(project, Battles.Units, status, navigate)
        {
            Describe = Describe,
            // Pasting a unit onto another keeps the target's tile, or they'd all stand in one place.
            PasteKeeps = new HashSet<string> { "X", "Y", "FacingDirection", "Elevation" },
            Actions =
            [
                ("Random gear", "Secondary action, reaction, support, movement and all equipment: <Random>",
                    [("SecondaryAction", "254"), ("Reaction", "510"), ("Support", "510"), ("Movement", "510"),
                     ("Head", "254"), ("Body", "254"), ("Accessory", "254"), ("RightHand", "254"), ("LeftHand", "254")]),
            ],
        };

        var left = new DockPanel { Width = 260, Margin = new Thickness(8, 8, 0, 8) };
        DockPanel.SetDock(_filter, Dock.Top);
        DockPanel.SetDock(_changedOnly, Dock.Top);
        left.Children.Add(_filter);
        left.Children.Add(_changedOnly);
        left.Children.Add(_battles);
        var mapBar = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(8, 8, 8, 4) };
        mapBar.Children.Add(new TextBlock { Text = "Map:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        mapBar.Children.Add(_mapPicker);
        mapBar.Children.Add(_upper);
        mapBar.Children.Add(_mapNote);
        var mapPanel = new DockPanel();
        DockPanel.SetDock(mapBar, Dock.Top);
        mapPanel.Children.Add(mapBar);
        DockPanel.SetDock(_away, Dock.Bottom);
        mapPanel.Children.Add(_away);
        mapPanel.Children.Add(new ScrollViewer
        {
            Content = _grid,
            MaxHeight = 430,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 0, 8, 4),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        });
        var right = new DockPanel();
        DockPanel.SetDock(mapPanel, Dock.Top);
        right.Children.Add(mapPanel);
        right.Children.Add(Units);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;

        if (maps is null)
        {
            _mapPicker.IsEnabled = false;
            _upper.IsEnabled = false;
            _mapNote.Text = "Maps need the installed game (Find game…).";
        }
        else
            _mapPicker.ItemsSource = new[] { "(none)" }.Concat(maps.Maps.Select(m => $"{m:000}  {maps.MapName(m) ?? "(no name)"}")).ToList();
        _mapPicker.SelectionChanged += (_, _) =>
        {
            if (_picking || _maps is null || CurrentBattle is not { } battle)
                return;
            // A map picked by hand is remembered for this battle on this PC.
            if (_mapPicker.SelectedIndex > 0)
                _choices[battle] = _maps.Maps[_mapPicker.SelectedIndex - 1];
            else
                _choices.Remove(battle);
            BattleMaps.SaveChoices(_choices);
            ShowMap();
        };
        _upper.IsCheckedChanged += (_, _) => _grid.ShowUpper = _upper.IsChecked == true;
        _grid.TileClicked += MoveUnit;
        Units.RecordShown += id =>
        {
            _grid.Selected = id % Battles.UnitsPerBattle + 1;
            // Follow the selected unit to its level.
            var upper = _project.Current(Battles.Units, id, "Elevation").Contains("UpperLevel");
            if (_upper.IsChecked != upper)
                _upper.IsChecked = upper;
            ShowUnits();
        };
        _project.Changed += ShowUnits;

        _filter.TextChanged += (_, _) => Fill();
        _changedOnly.IsCheckedChanged += (_, _) => Fill();
        _battles.SelectionChanged += (_, _) =>
        {
            if (_battles.SelectedIndex >= 0 && _battles.SelectedIndex < _shown.Count)
            {
                Units.ShowRange(_shown[_battles.SelectedIndex] * Battles.UnitsPerBattle, Battles.UnitsPerBattle);
                ShowMap();
            }
        };
        _project.Changed += () => _stale = true;
        AttachedToVisualTree += (_, _) =>
        {
            if (_stale)
            {
                _stale = false;
                Fill();
            }
        };
        Fill();
        _battles.SelectedIndex = 0;
    }

    private int? CurrentBattle => _battles.SelectedIndex >= 0 && _battles.SelectedIndex < _shown.Count ? _shown[_battles.SelectedIndex] : null;

    // The battle's map: the user's pick for it, or a guess from its name.
    private void ShowMap()
    {
        if (_maps is null || CurrentBattle is not { } battle)
            return;
        int? map = _choices.TryGetValue(battle, out var chosen) ? chosen : _maps.Guess(Names.Get(Names.Battles, battle));
        _picking = true;
        _mapPicker.SelectedIndex = map is { } m && _maps.Maps.IndexOf(m) is >= 0 and var i ? i + 1 : 0;
        _picking = false;
        _grid.Terrain = map is { } shown ? _maps.Load(shown) : null;
        _mapNote.Text = map is null ? "Pick the map this battle is fought on."
            : _choices.ContainsKey(battle) ? "(your choice)"
            : "(guessed from the battle's name; pick another if it's wrong)";
        ShowUnits();
    }

    private void ShowUnits()
    {
        if (CurrentBattle is not { } battle)
            return;
        var units = new List<MapGrid.Unit>();
        for (var slot = 0; slot < Battles.UnitsPerBattle; slot++)
        {
            var id = battle * Battles.UnitsPerBattle + slot;
            string Current(string field) => _project.Current(Battles.Units, id, field);
            if (Current("SpriteSet") == "0")
                continue; // empty slot
            units.Add(new MapGrid.Unit(slot + 1, int.Parse(Current("X")), int.Parse(Current("Y")),
                Current("Elevation").Contains("UpperLevel"), int.Parse(Current("TeamColor")), Picture(id)));
        }
        _grid.Units = units;
        // Units the map can't show: placed by the player's formation, or off the map.
        var away = units.Where(u => _grid.Terrain is not { } t || !t.Contains(u.X, u.Y)).Select(u => u.Number).ToList();
        _away.Text = _grid.Terrain is null || away.Count == 0 ? ""
            : $"Not on the map: unit {string.Join(", ", away)} (e.g. placed by your formation, or outside the map)";
    }

    // A unit's sprite (first frame, in its palette), as the mod has it; pictures are kept per sheet and palette.
    private readonly Dictionary<(int, int), Avalonia.Media.Imaging.Bitmap?> _pictures = [];

    private Avalonia.Media.Imaging.Bitmap? Picture(int id)
    {
        if (_maps is null)
            return null;
        string Current(string field) => _project.Current(Battles.Units, id, field);
        var job = int.Parse(Current("Job"));
        var jobKnown = _project.HasTable(Jobs.Schema.Name) && job < Jobs.Schema.RecordCount;
        if (Core.Sprites.UnitSprite.Of(int.Parse(Current("SpriteSet")), Current("Flags1").Contains("Female"), job, int.Parse(Current("Palette")),
                jobKnown ? int.Parse(_project.Current(Jobs.Schema, job, "MPortrait")) : 0,
                jobKnown ? int.Parse(_project.Current(Jobs.Schema, job, "MPalette")) : 0) is not { } sprite)
            return null;
        if (_pictures.TryGetValue(sprite, out var known))
            return known;
        Avalonia.Media.Imaging.Bitmap? picture = null;
        if (_maps.Sheet(sprite.Sprite) is { } sheet)
        {
            var rgba = Core.Sprites.UnitSprite.FirstFrame(sheet, sprite.Palette);
            var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(Core.Sprites.UnitSprite.FrameWidth, Core.Sprites.UnitSprite.FrameHeight),
                new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using (var buffer = bitmap.Lock())
            {
                const int Row = Core.Sprites.UnitSprite.FrameWidth * 4;
                for (var y = 0; y < Core.Sprites.UnitSprite.FrameHeight; y++)
                    System.Runtime.InteropServices.Marshal.Copy(rgba, y * Row, buffer.Address + y * buffer.RowBytes, Row);
            }
            picture = bitmap;
        }
        return _pictures[sprite] = picture;
    }

    // Clicking a tile puts the selected unit there (on the level shown).
    private void MoveUnit(int x, int y, bool upper)
    {
        if (Units.Current is not { } id)
            return;
        _project.Set(Battles.Units, id, "X", x.ToString());
        _project.Set(Battles.Units, id, "Y", y.ToString());
        _project.Set(Battles.Units, id, "Elevation", upper ? "UpperLevel" : "");
        _status($"{Describe(id)} moved to X {x}, Y {y}{(upper ? " (upper level)" : "")}");
        Units.Refresh();
    }

    private bool IsChanged(int battle) =>
        Enumerable.Range(battle * Battles.UnitsPerBattle, Battles.UnitsPerBattle).Any(id => _project.IsChanged(Battles.Units, id));

    private string Label(int battle) =>
        $"{battle:X3}  {Names.Get(Names.Battles, battle) ?? ""}{(IsChanged(battle) ? " •" : "")}";

    private void Fill()
    {
        var selected = _battles.SelectedIndex >= 0 && _battles.SelectedIndex < _shown.Count ? _shown[_battles.SelectedIndex] : (int?)null;
        var filter = _filter.Text?.Trim() ?? "";
        _shown.Clear();
        _shown.AddRange(Enumerable.Range(0, Battles.Count)
            .Where(b => filter.Length == 0 || Label(b).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Where(b => _changedOnly.IsChecked != true || IsChanged(b)));
        _battles.ItemsSource = _shown.Select(Label).ToList();
        if (selected is { } battle && _shown.IndexOf(battle) is >= 0 and var index)
            _battles.SelectedIndex = index;
    }

    // "Unit 3: Delita, Squire", or "Unit 9: (empty)".
    private string Describe(int id)
    {
        string Current(string field) => _project.Current(Battles.Units, id, field);
        var unit = $"Unit {id % Battles.UnitsPerBattle + 1}";
        var sprite = int.Parse(Current("SpriteSet"));
        if (sprite == 0)
            return $"{unit}: (empty)";
        // A story character's name; generic units ("<Random>", or sprite sets like "Monster") go by job alone.
        var special = Names.Get(Names.SpecialNames, int.Parse(Current("SpecialName")));
        var who = special is { Length: > 0 } && !special.StartsWith('<') ? special : null;
        var job = Names.Get(Names.Jobs, int.Parse(Current("Job")));
        return (who, job) switch
        {
            (not null, not null) => $"{unit}: {who}, {job}",
            (not null, null) => $"{unit}: {who}",
            (null, not null) => $"{unit}: {job}",
            _ => $"{unit}: {Names.Get(Names.SpriteSets, sprite) ?? $"sprite {sprite:X2}"}",
        };
    }
}
