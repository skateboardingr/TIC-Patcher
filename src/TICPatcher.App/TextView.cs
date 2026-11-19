using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Text;

namespace TICPatcher.App;

/// <summary>
/// Edits a text table that has no stat table behind it, e.g. unit names
/// (charaname.nxd): rows on the left, each text field on the right. Empty rows
/// the game doesn't use are hidden unless the mod gives them text.
/// </summary>
public sealed class TextView : UserControl
{
    private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.Parse("#D9822B"));

    private readonly ModProject _project;
    private readonly TextTable _table;
    private readonly Action<string> _status;
    private readonly ListBox _rows = new();
    private readonly TextBox _filter = new() { PlaceholderText = "Filter" };
    private readonly CheckBox _changedOnly = new() { Content = "Changed only", Margin = new Thickness(8, 0, 0, 0) };
    private readonly StackPanel _fields = new() { Spacing = 6, Margin = new Thickness(12, 0, 12, 12) };
    private readonly List<int> _shown = [];
    private bool _building;

    public TextView(ModProject project, TextTable table, Action<string> status)
    {
        _project = project;
        _table = table;
        _status = status;

        var left = new DockPanel { Width = 280 };
        var search = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_changedOnly, Dock.Right);
        search.Children.Add(_changedOnly);
        search.Children.Add(_filter);
        DockPanel.SetDock(search, Dock.Top);
        left.Children.Add(search);
        left.Children.Add(_rows);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8) };
        grid.Children.Add(left);
        var right = new ScrollViewer { Content = _fields };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;

        _filter.TextChanged += (_, _) => FillRows();
        _changedOnly.IsCheckedChanged += (_, _) => FillRows();
        _rows.SelectionChanged += (_, _) => ShowRow();
        FillRows();
        _rows.SelectedIndex = 0;
    }

    private bool IsChanged(int id) => _table.Fields.Any(f => _project.IsTextChanged(_table, id, f.Name));

    private string Label(int id)
    {
        var name = _project.CurrentText(_table, id, _table.Fields[0].Name);
        var mark = IsChanged(id) ? " •" : "";
        return string.IsNullOrEmpty(name) ? $"{id:X3}{mark}" : $"{id:X3}  {name}{mark}";
    }

    private void FillRows()
    {
        var selected = SelectedId;
        var filter = _filter.Text?.Trim() ?? "";
        _shown.Clear();
        _shown.AddRange(Enumerable.Range(0, _table.Rows)
            .Where(id => _table.Fields.Any(f => !string.IsNullOrEmpty(_project.CurrentText(_table, id, f.Name))))
            .Where(id => filter.Length == 0 || Label(id).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Where(id => _changedOnly.IsChecked != true || IsChanged(id)));
        _building = true;
        _rows.ItemsSource = _shown.Select(Label).ToList();
        _building = false;
        if (selected is { } id && _shown.IndexOf(id) is >= 0 and var index)
            _rows.SelectedIndex = index;
    }

    private int? SelectedId => _rows.SelectedIndex >= 0 && _rows.SelectedIndex < _shown.Count ? _shown[_rows.SelectedIndex] : null;

    private void ShowRow()
    {
        if (_building || SelectedId is not { } id)
            return;
        _building = true;
        _fields.Children.Clear();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var revert = new Button { Content = "Revert", IsEnabled = IsChanged(id) };
        revert.Click += (_, _) =>
        {
            _project.RevertText(_table, id);
            FillRows();
            ShowRow();
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

        foreach (var field in _table.Fields)
            _fields.Children.Add(FieldRow(id, field.Name));
        _building = false;
    }

    private Control FieldRow(int id, string field)
    {
        var changed = _project.IsTextChanged(_table, id, field);
        var label = new TextBlock
        {
            Text = TableEditor.Humanize(field),
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = changed ? FontWeight.Bold : FontWeight.Normal,
        };
        if (changed)
            label.Foreground = ChangedBrush;
        ToolTip.SetTip(label, $"Original: {_project.OriginalText(_table, id, field)}\nIn mods (tables.json): Text / {_table.Name} / {field}");
        var box = new TextBox
        {
            Text = _project.CurrentText(_table, id, field) ?? "",
            MinWidth = 260,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        void Save()
        {
            var value = box.Text ?? "";
            if (_building || value == (_project.CurrentText(_table, id, field) ?? ""))
                return;
            _project.SetText(_table, id, field, value);
            _status($"{_table.Name} 0x{id:X3} {field} = {value}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                FillRows();
                ShowRow();
            });
        }
        box.LostFocus += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                Save();
        };
        var row = new DockPanel();
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(box);
        return row;
    }
}
