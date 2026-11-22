using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Sprites;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.App;

/// <summary>
/// Battle sprites: pick a sprite and one of its 16 palettes, see the unit and its whole
/// sheet in those colours, and recolour the palette. Colours are the game's own 5 bits
/// per channel (0-31); colour 0 is see-through. Palette changes go in the mod with the
/// other table edits (SpritePalettes) and show at once on the battle map and in previews.
/// </summary>
public sealed class SpritesView : UserControl
{
    private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.Parse("#D9822B"));

    private readonly ModProject _project;
    private readonly SpritePictures _pictures;
    private readonly Action<string> _status;
    private readonly ListBox _sprites = new();
    private readonly TextBox _filter = new() { PlaceholderText = "Filter sprites", Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel _right = new() { Spacing = 10, Margin = new Thickness(12, 8, 12, 12) };
    private readonly List<int> _shown = [];
    private int _palette;
    private int _colour = 1;
    private bool _building;

    public SpritesView(ModProject project, SpritePictures pictures, Action<string> status)
    {
        _project = project;
        _pictures = pictures;
        _status = status;

        var left = new DockPanel { Width = 260, Margin = new Thickness(8) };
        DockPanel.SetDock(_filter, Dock.Top);
        left.Children.Add(_filter);
        left.Children.Add(_sprites);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(left);
        var scroll = new ScrollViewer { Content = _right, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;

        _filter.TextChanged += (_, _) => Fill();
        _sprites.SelectionChanged += (_, _) => Show();
        Fill();
        _sprites.SelectedIndex = 0;
    }

    private int? Sprite => _sprites.SelectedIndex >= 0 && _sprites.SelectedIndex < _shown.Count ? _shown[_sprites.SelectedIndex] : null;

    private bool IsChanged(int sprite, int palette) => _project.IsChanged(SpriteSheets.Palettes, sprite * 16 + palette);

    private string Label(int sprite) =>
        $"{sprite:X2}  {Names.Get(Names.SpriteFiles, sprite) ?? ""}{(Enumerable.Range(0, 16).Any(p => IsChanged(sprite, p)) ? " •" : "")}";

    private void Fill()
    {
        var selected = Sprite;
        var filter = _filter.Text?.Trim() ?? "";
        _shown.Clear();
        _shown.AddRange(Enumerable.Range(1, SpriteSheets.Count)
            .Where(s => UnitSprite.FileOf(s) is not null && _pictures.Maps.Sheet(s) is not null)
            .Where(s => filter.Length == 0 || Label(s).Contains(filter, StringComparison.OrdinalIgnoreCase)));
        _building = true;
        _sprites.ItemsSource = _shown.Select(Label).ToList();
        _building = false;
        if (selected is { } s && _shown.IndexOf(s) is >= 0 and var i)
            _sprites.SelectedIndex = i;
    }

    private void Show()
    {
        if (_building || Sprite is not { } sprite)
            return;
        _right.Children.Clear();
        _right.Children.Add(new TextBlock { Text = $"{Names.Get(Names.SpriteFiles, sprite)} (sprite {sprite:X2}, {UnitSprite.FileOf(sprite)})", FontSize = 18, FontWeight = FontWeight.SemiBold });

        // The 16 palettes, each a strip of its colours; click one to show and edit it.
        var palettes = new WrapPanel();
        for (var p = 0; p < 16; p++)
        {
            var palette = p;
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var colour in _pictures.Colours(sprite, palette).Skip(1))
                strip.Children.Add(new Border { Width = 5, Height = 14, Background = Brush(colour) });
            var button = new Button
            {
                Content = new StackPanel { Children = { new TextBlock { Text = $"Palette {palette}{(IsChanged(sprite, palette) ? " •" : "")}", FontSize = 11 }, strip } },
                Margin = new Thickness(0, 0, 6, 6),
                BorderBrush = palette == _palette ? Brushes.Orange : null,
                BorderThickness = new Thickness(palette == _palette ? 2 : 0),
            };
            button.Click += (_, _) =>
            {
                _palette = palette;
                Show();
            };
            palettes.Children.Add(button);
        }
        _right.Children.Add(palettes);

        // This palette's colours: click one to edit it below.
        var colours = _pictures.Colours(sprite, _palette);
        var record = sprite * 16 + _palette;
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            var changed = _project.IsChanged(SpriteSheets.Palettes, record, $"Color{i}");
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                Background = i == 0 ? Brushes.Transparent : Brush(colours[i]),
                BorderBrush = i == _colour ? Brushes.Orange : changed ? ChangedBrush : Brushes.Gray,
                BorderThickness = new Thickness(i == _colour ? 3 : changed ? 2 : 1),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = i == 0 ? new TextBlock { Text = "∅", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 } : null,
            };
            ToolTip.SetTip(swatch, i == 0 ? "Colour 0: see-through" : $"Colour {i}: {Describe(colours[i])}{(changed ? $"\nOriginal: {Describe(ushort.Parse(_project.Original(SpriteSheets.Palettes, record, $"Color{i}")))}" : "")}");
            swatch.PointerPressed += (_, _) =>
            {
                _colour = index;
                Show();
            };
            swatches.Children.Add(swatch);
        }
        _right.Children.Add(swatches);
        if (_colour > 0)
            _right.Children.Add(ColourEditor(sprite, record, colours[_colour]));

        var revert = new Button { Content = "Revert this palette", IsEnabled = IsChanged(sprite, _palette) };
        revert.Click += (_, _) =>
        {
            _project.Revert(SpriteSheets.Palettes, record);
            Refresh();
        };
        _right.Children.Add(revert);

        // The unit standing, and its whole sheet, in this palette.
        var pictures = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        pictures.Children.Add(Framed(SpritePictures.Show(_pictures.Frame(sprite, _palette), 4)));
        pictures.Children.Add(Framed(SpritePictures.Show(_pictures.Sheet(sprite, _palette), 2)));
        _right.Children.Add(pictures);
        _right.Children.Add(new TextBlock
        {
            Text = "The sheet's first 256 rows; the rest of each sheet is stored compressed and isn't shown yet. Changed palettes are saved with the mod, but aren't applied in the game yet.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            MaxWidth = 700,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
    }

    // Red, green and blue of the selected colour, 0-31 each.
    private Control ColourEditor(int sprite, int record, ushort colour)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = $"Colour {_colour}:", VerticalAlignment = VerticalAlignment.Center });
        var parts = new[] { ("Red", 0), ("Green", 5), ("Blue", 10) };
        foreach (var (name, shift) in parts)
        {
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            var box = new NumericUpDown { Value = (colour >> shift) & 31, Minimum = 0, Maximum = 31, Increment = 1, FormatString = "0", Width = 110 };
            box.ValueChanged += (_, _) =>
            {
                if (box.Value is not { } v)
                    return;
                var now = ushort.Parse(_project.Current(SpriteSheets.Palettes, record, $"Color{_colour}"));
                var next = (ushort)((now & ~(31 << shift)) | ((int)v << shift));
                _project.Set(SpriteSheets.Palettes, record, $"Color{_colour}", next.ToString());
                _status($"{Names.Get(Names.SpriteFiles, sprite)} palette {_palette} colour {_colour} = {Describe(next)}");
                // Redraw once typing settles (a redraw replaces this box).
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!box.IsKeyboardFocusWithin)
                        Refresh();
                }, Avalonia.Threading.DispatcherPriority.Background);
            };
            box.LostFocus += (_, _) => Refresh();
            row.Children.Add(box);
        }
        return row;
    }

    private void Refresh()
    {
        var sprite = Sprite;
        Fill();
        if (Sprite == sprite)
            Show();
    }

    private static Border Framed(Control content) => new()
    {
        Child = content,
        Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
        Padding = new Thickness(6),
        VerticalAlignment = VerticalAlignment.Top,
    };

    private static IBrush Brush(ushort colour)
    {
        var (r, g, b) = UnitSprite.Rgb(colour);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private static string Describe(ushort colour) => $"red {colour & 31}, green {(colour >> 5) & 31}, blue {(colour >> 10) & 31}";
}
