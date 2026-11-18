using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.App;

/// <summary>
/// Shops by town: pick a shop and tick what it sells. The game stores it the
/// other way round (each item lists its shops); this edits the same data.
/// </summary>
public sealed class ShopView : UserControl
{
    private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.Parse("#D9822B"));

    private readonly ModProject _project;
    private readonly Action<string> _status;
    private readonly ComboBox _shop = new() { MinWidth = 180 };
    private readonly TextBox _filter = new() { PlaceholderText = "Filter items", Width = 200 };
    private readonly CheckBox _soldOnly = new() { Content = "Sold here only" };
    private readonly WrapPanel _items = new();
    private readonly List<string> _shops;
    private bool _building;
    private bool _stale;

    public ShopView(ModProject project, Action<string> status)
    {
        _project = project;
        _status = status;
        _shops = Items.Shops.OfType<string>().Where(s => s != "None").ToList();
        _shop.ItemsSource = _shops;
        _shop.SelectedIndex = _shops.IndexOf("Dorter");

        var all = new Button { Content = "All" };
        all.Click += (_, _) => SetAll(true);
        var clear = new Button { Content = "Clear" };
        clear.Click += (_, _) => SetAll(false);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(new TextBlock { Text = "Shop:", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_shop);
        bar.Children.Add(_filter);
        bar.Children.Add(_soldOnly);
        bar.Children.Add(all);
        bar.Children.Add(clear);

        var root = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(new ScrollViewer { Content = _items });
        Content = root;

        _shop.SelectionChanged += (_, _) => Fill();
        _filter.TextChanged += (_, _) => Fill();
        _soldOnly.IsCheckedChanged += (_, _) => Fill();
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
    }

    private string Shop => _shops[Math.Max(0, _shop.SelectedIndex)];

    private bool SoldAt(int item) =>
        _project.Current(Items.ShopInventories, item, "Shops").Split(", ").Contains(Shop);

    private IEnumerable<int> Shown()
    {
        var filter = _filter.Text?.Trim() ?? "";
        return Enumerable.Range(Items.ShopInventories.FirstId, Items.ShopInventories.RecordCount)
            .Where(item => Names.Get(Names.Items, item) is { } name && name != "<Nothing>"
                && (filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                && (_soldOnly.IsChecked != true || SoldAt(item)));
    }

    private void Fill()
    {
        _building = true;
        _items.Children.Clear();
        var count = 0;
        foreach (var item in Shown())
        {
            var box = new CheckBox
            {
                Content = $"{item:X2}  {Names.Get(Names.Items, item)}",
                IsChecked = SoldAt(item),
                Width = 230,
            };
            if (_project.IsChanged(Items.ShopInventories, item, "Shops"))
                box.Foreground = ChangedBrush;
            var id = item;
            box.IsCheckedChanged += (_, _) =>
            {
                if (!_building)
                    Set(id, box.IsChecked == true);
            };
            _items.Children.Add(box);
            count += SoldAt(item) ? 1 : 0;
        }
        _building = false;
        _status($"{Shop} sells {count} of the items shown.");
    }

    private void Set(int item, bool sold)
    {
        var shops = _project.Current(Items.ShopInventories, item, "Shops").Split(", ", StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (sold)
            shops.Add(Shop);
        else
            shops.Remove(Shop);
        _project.Set(Items.ShopInventories, item, "Shops", string.Join(", ", shops));
    }

    private void SetAll(bool sold)
    {
        foreach (var item in Shown().ToList())
            Set(item, sold);
        Fill();
    }
}
