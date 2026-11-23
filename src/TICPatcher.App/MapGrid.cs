using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TICPatcher.Core.Maps;
using Sprites = TICPatcher.Core.Sprites.UnitSprite;

namespace TICPatcher.App;

/// <summary>
/// A battle map seen from above: one square per tile, coloured by its terrain and
/// lighter the higher it is, with the height written on it. Units are circles in
/// their team colour, numbered as in the unit list; the selected one is outlined.
/// Clicking a tile moves the selected unit there.
/// </summary>
public sealed class MapGrid : Control
{
    /// <summary>A unit to draw: its number in the battle (1-16), tile, level and team colour (0-3).</summary>
    public sealed record Unit(int Number, int X, int Y, bool Upper, int Team, IImage? Picture = null);

    private MapTerrain? _terrain;
    private IReadOnlyList<Unit> _units = [];
    private int? _selected;
    private bool _showUpper;

    /// <summary>A terrain name for a surface type (TIC's land.nxd), for the hover text.</summary>
    public Func<int, string?> TerrainName { get; init; } = _ => null;

    /// <summary>A tile was clicked: (x, y, upper level).</summary>
    public event Action<int, int, bool>? TileClicked;

    public MapTerrain? Terrain
    {
        get => _terrain;
        set { _terrain = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    public IReadOnlyList<Unit> Units
    {
        get => _units;
        set { _units = value; InvalidateVisual(); }
    }

    /// <summary>The selected unit's number, outlined.</summary>
    public int? Selected
    {
        get => _selected;
        set { _selected = value; InvalidateVisual(); }
    }

    /// <summary>Show the upper level (walkways, bridges) instead of the lower.</summary>
    public bool ShowUpper
    {
        get => _showUpper;
        set { _showUpper = value; InvalidateVisual(); }
    }

    // Tiles shrink on long maps so the whole map fits without scrolling.
    private double Cell => _terrain is null ? 30 : Math.Clamp(Math.Floor(420.0 / _terrain.Length), 18, 30);

    protected override Size MeasureOverride(Size availableSize) =>
        _terrain is null ? new Size(0, 0) : new Size(_terrain.Width * Cell + 1, _terrain.Length * Cell + 1);

    public MapGrid()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, e) =>
        {
            if (TileAt(e.GetPosition(this)) is { } tile)
                TileClicked?.Invoke(tile.X, tile.Y, _showUpper);
        };
        PointerMoved += (_, e) =>
        {
            if (_terrain is null || TileAt(e.GetPosition(this)) is not { } tile)
            {
                ToolTip.SetTip(this, null);
                return;
            }
            var t = _terrain.At(tile.X, tile.Y, _showUpper);
            var name = TerrainName(t.Surface) ?? $"terrain {t.Surface}";
            var who = _units.Where(u => u.X == tile.X && u.Y == tile.Y && u.Upper == _showUpper).Select(u => $"unit {u.Number}");
            ToolTip.SetTip(this, t.IsEmpty
                ? $"X {tile.X}, Y {tile.Y}: no tile"
                : $"X {tile.X}, Y {tile.Y}: {name}, height {t.Height}{(t.Depth > 0 ? $", water depth {t.Depth}" : "")}{(who.Any() ? $"\n{string.Join(", ", who)}" : "")}");
        };
    }

    private (int X, int Y)? TileAt(Point p)
    {
        if (_terrain is null)
            return null;
        int x = (int)(p.X / Cell), y = (int)(p.Y / Cell);
        return _terrain.Contains(x, y) ? (x, y) : null;
    }

    public override void Render(DrawingContext context)
    {
        if (_terrain is null)
            return;
        var maxHeight = Math.Max(1, _terrain.Lower.Concat(_terrain.Upper).Max(t => t.Height));
        var typeface = new Typeface(FontFamily.Default);
        for (var y = 0; y < _terrain.Length; y++)
            for (var x = 0; x < _terrain.Width; x++)
            {
                var tile = _terrain.At(x, y, _showUpper);
                var rect = new Rect(x * Cell, y * Cell, Cell, Cell);
                if (tile.IsEmpty)
                {
                    context.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), null, rect);
                    continue;
                }
                context.DrawRectangle(new SolidColorBrush(Shade(SurfaceColor(tile), tile.Height / (double)maxHeight)), new Pen(Brushes.Black, 0.5), rect);
                var label = new FormattedText(tile.Height.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 10, Brushes.Black);
                context.DrawText(label, new Point(rect.X + 2, rect.Y + 1));
                if (tile.Depth > 0)
                    context.DrawText(new FormattedText("~", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, Brushes.Navy),
                        new Point(rect.Right - 9, rect.Y));
            }
        // Units sharing a tile (random battles list alternatives for one spot) are one marker, "2·7".
        foreach (var stack in _units.Where(u => _terrain.Contains(u.X, u.Y)).GroupBy(u => (u.X, u.Y, u.Upper)))
        {
            var units = stack.ToList();
            var first = units[0];
            var centre = new Point(first.X * Cell + Cell / 2, first.Y * Cell + Cell / 2 + 3);
            var other = first.Upper != _showUpper; // on the other level: drawn faintly
            var fill = TeamColor(first.Team, (byte)(other ? 90 : 230));
            var selected = units.Any(u => u.Number == _selected);
            var text = units.Count switch
            {
                1 => first.Number.ToString(),
                2 => $"{units[0].Number}·{units[1].Number}",
                _ => $"{first.Number}+{units.Count - 1}",
            };
            var number = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, Brushes.White);
            if (first.Picture is { } picture)
            {
                // The unit itself, standing on its tile (a little taller than the tile), and its number below.
                // Scaled so a person (about 40 pixels tall) is a little taller than a tile; big monsters are bigger.
                var height = Cell * 1.25 * Math.Min(picture.Size.Height / 40.0, 1.8);
                var width = height * picture.Size.Width / picture.Size.Height;
                using (context.PushOpacity(other ? 0.35 : 1))
                    context.DrawImage(picture, new Rect(first.X * Cell + (Cell - width) / 2, first.Y * Cell + Cell - height, width, height));
                var badge = new Rect(first.X * Cell + Cell - number.Width - 4, first.Y * Cell + Cell - number.Height, number.Width + 4, number.Height);
                context.DrawRectangle(fill, new Pen(selected ? Brushes.Yellow : Brushes.White, selected ? 2 : 0.5), badge, 3, 3);
                context.DrawText(number, new Point(badge.X + 2, badge.Y));
                continue;
            }
            var radius = Math.Max(Cell * 0.3, number.Width / 2 + 3);
            context.DrawEllipse(fill, new Pen(selected ? Brushes.Yellow : Brushes.White, selected ? 3 : 1), centre, radius, Math.Min(radius, Cell * 0.32));
            context.DrawText(number, new Point(centre.X - number.Width / 2, centre.Y - number.Height / 2));
        }
        if (_selected is { } s && _units.FirstOrDefault(u => u.Number == s) is { } chosen && _terrain.Contains(chosen.X, chosen.Y))
            context.DrawRectangle(null, new Pen(Brushes.Yellow, 3), new Rect(chosen.X * Cell + 1.5, chosen.Y * Cell + 1.5, Cell - 3, Cell - 3));
    }

    // Terrain groups (TIC's land.nxd rows): water, plants, sand and earth, stone, wood, snow, lava.
    private static Color SurfaceColor(MapTile tile) => tile.Surface switch
    {
        _ when tile.Depth > 0 => Color.Parse("#4F86C6"),
        9 or 10 or 11 or 14 or 15 or 16 or 17 or 45 => Color.Parse("#4F86C6"),
        3 or 4 or 30 or 36 or 39 or 43 => Color.Parse("#6FA552"),
        0 or 1 or 7 or 8 or 26 => Color.Parse("#C9A86A"),
        2 or 6 or 12 or 19 or 21 or 23 or 32 or 34 or 44 or 47 or 63 => Color.Parse("#9A9A9A"),
        20 or 22 or 27 or 29 or 31 or 33 or 35 or 37 or 38 or 40 or 46 => Color.Parse("#A0723F"),
        5 or 13 => Color.Parse("#E4EEF5"),
        18 => Color.Parse("#D2513A"),
        41 or 42 => Color.Parse("#7A8590"),
        24 or 25 or 28 => Color.Parse("#303030"),
        _ => Color.Parse("#B0A090"),
    };

    // Higher is lighter.
    private static Color Shade(Color c, double height)
    {
        var f = 0.55 + 0.45 * Math.Clamp(height, 0, 1);
        return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    }

    private static IBrush TeamColor(int team, byte alpha) => new SolidColorBrush(team switch
    {
        1 => Color.FromArgb(alpha, 200, 40, 40),
        2 => Color.FromArgb(alpha, 40, 150, 60),
        3 => Color.FromArgb(alpha, 70, 170, 220),
        _ => Color.FromArgb(alpha, 40, 70, 200),
    });
}
