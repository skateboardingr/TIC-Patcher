using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;

namespace TICPatcher.App;

/// <summary>
/// Edits one table: its records on the left, the selected record's fields on
/// the right, built from the table's schema. Changed fields are highlighted
/// and show the original value on hover.
/// </summary>
public sealed class TableEditor : UserControl
{
    private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.Parse("#D9822B"));

    private readonly ModProject _project;
    private readonly TableSchema _schema;
    private readonly Action<string> _status;
    private readonly ListBox _records = new();
    private readonly TextBox _filter = new() { PlaceholderText = "Filter" };
    private readonly StackPanel _fields = new() { Spacing = 6, Margin = new Thickness(12, 0, 12, 12) };
    private readonly List<int> _shown = [];
    private bool _building;

    public TableEditor(ModProject project, TableSchema schema, Action<string> status)
    {
        _project = project;
        _schema = schema;
        _status = status;

        var left = new DockPanel { Width = 260 };
        DockPanel.SetDock(_filter, Dock.Top);
        _filter.Margin = new Thickness(0, 0, 0, 6);
        left.Children.Add(_filter);
        left.Children.Add(_records);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8) };
        grid.Children.Add(left);
        var right = new ScrollViewer { Content = _fields };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;

        _filter.TextChanged += (_, _) => FillRecords();
        _records.SelectionChanged += (_, _) => ShowRecord();
        FillRecords();
        _records.SelectedIndex = 0;
    }

    private string Label(int id)
    {
        var name = _schema.NameOf(id);
        var mark = _project.IsChanged(_schema, id) ? " •" : "";
        var hex = _schema.FirstId + _schema.RecordCount > 0x100 ? $"{id:X3}" : $"{id:X2}";
        return name is null ? $"{hex}{mark}" : $"{hex}  {name}{mark}";
    }

    private void FillRecords()
    {
        var selected = SelectedId;
        var filter = _filter.Text?.Trim() ?? "";
        _shown.Clear();
        _shown.AddRange(Enumerable.Range(_schema.FirstId, _schema.RecordCount)
            .Where(id => filter.Length == 0 || Label(id).Contains(filter, StringComparison.OrdinalIgnoreCase)));
        _records.ItemsSource = _shown.Select(Label).ToList();
        if (selected is { } id && _shown.IndexOf(id) is >= 0 and var index)
            _records.SelectedIndex = index;
    }

    private int? SelectedId => _records.SelectedIndex >= 0 && _records.SelectedIndex < _shown.Count ? _shown[_records.SelectedIndex] : null;

    private void RefreshLabel(int id)
    {
        var index = _shown.IndexOf(id);
        if (index < 0)
            return;
        var labels = _shown.Select(Label).ToList();
        _building = true;
        _records.ItemsSource = labels;
        _records.SelectedIndex = index;
        _building = false;
    }

    private void ShowRecord()
    {
        if (_building || SelectedId is not { } id)
            return;
        _building = true;
        _fields.Children.Clear();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var revert = new Button { Content = "Revert record", IsEnabled = _project.IsChanged(_schema, id) };
        revert.Click += (_, _) =>
        {
            _project.Revert(_schema, id);
            RefreshLabel(id);
            ShowRecord();
        };
        DockPanel.SetDock(revert, Dock.Right);
        header.Children.Add(revert);
        header.Children.Add(new TextBlock
        {
            Text = Label(id).Replace(" •", ""),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _fields.Children.Add(header);

        foreach (var field in _schema.Fields)
            _fields.Children.Add(FieldRow(id, field));
        _building = false;
    }

    private Control FieldRow(int id, FieldDef field)
    {
        var changed = _project.IsChanged(_schema, id, field.Name);
        var label = new TextBlock
        {
            Text = field.Name,
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = changed ? FontWeight.Bold : FontWeight.Normal,
        };
        if (changed)
            label.Foreground = ChangedBrush;
        ToolTip.SetTip(label, $"Original: {Show(field, _project.Original(_schema, id, field.Name))}");

        var row = new DockPanel();
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(Editor(id, field));
        return row;
    }

    private static string Show(FieldDef field, string value) =>
        field.Kind == FieldKind.Flags ? (value.Length == 0 ? "(none)" : value) : value;

    private Control Editor(int id, FieldDef field)
    {
        var current = _project.Current(_schema, id, field.Name);
        var list = field.Kind == FieldKind.AbilityId ? Names.Abilities : field.NameList;

        if (field.Kind == FieldKind.Flags)
        {
            var set = current.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var panel = new WrapPanel();
            foreach (var name in (field.FlagNames ?? []).OfType<string>())
            {
                var box = new CheckBox { Content = name, IsChecked = set.Contains(name), Margin = new Thickness(0, 0, 12, 0) };
                box.IsCheckedChanged += (_, _) =>
                {
                    var on = panel.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Content!);
                    Commit(id, field, string.Join(", ", on));
                };
                panel.Children.Add(box);
            }
            return panel;
        }

        if (list is not null)
        {
            var max = field.Kind == FieldKind.Byte ? 0xFF : 0x1FF;
            var items = Enumerable.Range(0, max + 1).Select(i => max > 0xFF ? $"{i:X3}  {Names.Get(list, i) ?? ""}" : $"{i:X2}  {Names.Get(list, i) ?? ""}").ToList();
            var value = int.Parse(current);
            if (value <= max)
            {
                var combo = new ComboBox { ItemsSource = items, SelectedIndex = value, MinWidth = 260, MaxDropDownHeight = 400 };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedIndex >= 0)
                        Commit(id, field, combo.SelectedIndex.ToString());
                };
                return combo;
            }
        }

        var limit = field.Kind switch
        {
            FieldKind.Byte => 0xFF,
            FieldKind.UInt16 => 0xFFFF,
            FieldKind.Bits => field.Mask >> System.Numerics.BitOperations.TrailingZeroCount(field.Mask),
            _ => 0x1FF,
        };
        var number = new NumericUpDown
        {
            Value = decimal.Parse(current),
            Minimum = 0,
            Maximum = limit,
            Increment = 1,
            FormatString = "0",
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        number.ValueChanged += (_, _) =>
        {
            if (number.Value is { } v)
                Commit(id, field, ((int)v).ToString());
        };
        return number;
    }

    private void Commit(int id, FieldDef field, string value)
    {
        if (_building)
            return;
        try
        {
            _project.Set(_schema, id, field.Name, value);
            _status($"{_schema.Name} 0x{id:X2} {field.Name} = {value}");
        }
        catch (FormatException error)
        {
            _status(error.Message);
        }
        RefreshLabel(id);
        ShowRecord();
    }
}
