using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.App;

/// <summary>
/// Edits one table: its records on the left, the selected record's fields on
/// the right, built from the table's schema. Changed fields are highlighted
/// and show the original value on hover. Records can be copied and pasted
/// onto others of the same table, and the list can show changed records only.
/// </summary>
public sealed class TableEditor : UserControl
{
    private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.Parse("#D9822B"));

    private readonly ModProject _project;
    private readonly TableSchema _schema;
    private readonly Action<string> _status;
    private readonly Action<string, int> _navigate;
    private readonly ListBox _records = new();
    private readonly TextBox _filter = new() { PlaceholderText = "Filter" };
    private readonly CheckBox _changedOnly = new() { Content = "Changed only", Margin = new Thickness(8, 0, 0, 0) };

    // Shared by every table editor: a copied record, pasted onto one of the same table.
    private static (string Table, Dictionary<string, string> Values)? _clipboard;
    private readonly StackPanel _fields = new() { Spacing = 6, Margin = new Thickness(12, 0, 12, 12) };
    private readonly List<int> _shown = [];
    private bool _building;
    private bool _stale; // the mod changed while another tab was showing

    public TableEditor(ModProject project, TableSchema schema, Action<string> status, Action<string, int> navigate)
    {
        _project = project;
        _schema = schema;
        _status = status;
        _navigate = navigate;

        var left = new DockPanel { Width = 280 };
        var search = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_changedOnly, Dock.Right);
        search.Children.Add(_changedOnly);
        search.Children.Add(_filter);
        DockPanel.SetDock(search, Dock.Top);
        left.Children.Add(search);
        left.Children.Add(_records);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8) };
        grid.Children.Add(left);
        var right = new ScrollViewer { Content = _fields };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;

        // Labels, "Used by" lists and linked dropdowns depend on other tables,
        // so redraw when coming back to this tab after any change.
        _project.Changed += () => _stale = true;
        AttachedToVisualTree += (_, _) =>
        {
            if (!_stale)
                return;
            _stale = false;
            FillRecords();
            ShowRecord();
        };
        _filter.TextChanged += (_, _) => FillRecords();
        _changedOnly.IsCheckedChanged += (_, _) => FillRecords();
        _records.SelectionChanged += (_, _) => ShowRecord();
        FillRecords();
        _records.SelectedIndex = 0;
    }

    private string Label(int id)
    {
        var name = Describe?.Invoke(id) ?? (_schema.Name == Items.Attributes.Name ? AttributeUsers(id) : _schema.NameOf(id));
        var mark = _project.IsChanged(_schema, id) ? " •" : "";
        if (Describe is not null && name is not null)
            return $"{name}{mark}"; // described records name themselves (e.g. "Unit 3: …")
        var hex = _schema.FirstId + _schema.RecordCount > 0x100 ? $"{id:X3}" : $"{id:X2}";
        return name is null ? $"{hex}{mark}" : $"{hex}  {name}{mark}";
    }

    private void FillRecords()
    {
        var selected = SelectedId;
        var filter = _filter.Text?.Trim() ?? "";
        _shown.Clear();
        _shown.AddRange(Enumerable.Range(_range?.First ?? _schema.FirstId, _range?.Count ?? _schema.RecordCount)
            .Where(id => filter.Length == 0 || Label(id).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Where(id => _changedOnly.IsChecked != true || _project.IsChanged(_schema, id)));
        _records.ItemsSource = _shown.Select(Label).ToList();
        if (selected is { } id && _shown.IndexOf(id) is >= 0 and var index)
            _records.SelectedIndex = index;
    }

    /// <summary>Names records from their current values instead (e.g. a battle unit by its sprite and job).</summary>
    public Func<int, string?>? Describe { get; init; }

    /// <summary>Extra buttons beside Copy and Paste that set several fields at once (e.g. a battle unit's gear to random).</summary>
    public IReadOnlyList<(string Label, string Tip, IReadOnlyList<(string Field, string Value)> Values)> Actions { get; init; } = [];

    /// <summary>Fields Paste leaves as they are on the record pasted onto (e.g. a battle unit's tile).</summary>
    public IReadOnlySet<string> PasteKeeps { get; init; } = new HashSet<string>();

    // Only these records, e.g. one battle's 16 units (null: the whole table).
    private (int First, int Count)? _range;

    /// <summary>Lists only records <paramref name="first"/> to <paramref name="first"/> + <paramref name="count"/> - 1.</summary>
    public void ShowRange(int first, int count)
    {
        _range = (first, count);
        FillRecords();
        if (_records.SelectedIndex < 0 && _shown.Count > 0)
            _records.SelectedIndex = 0;
    }

    /// <summary>Shows a record, clearing any filter that would hide it.</summary>
    public void Select(int id)
    {
        if (!_shown.Contains(id))
        {
            _filter.Text = "";
            _changedOnly.IsChecked = false;
            FillRecords();
        }
        _records.SelectedIndex = _shown.IndexOf(id);
        _records.ScrollIntoView(_records.SelectedIndex);
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
        var revert = _revert = new Button { Content = "Revert record", IsEnabled = _project.IsChanged(_schema, id) };
        revert.Click += (_, _) =>
        {
            _project.Revert(_schema, id);
            RefreshLabel(id);
            ShowRecord();
        };
        var copy = new Button { Content = "Copy" };
        copy.Click += (_, _) =>
        {
            _clipboard = (_schema.Name, _schema.Fields.ToDictionary(f => f.Name, f => _project.Current(_schema, id, f.Name)));
            _status($"Copied {Label(id).Replace(" •", "")}");
            ShowRecord();
        };
        var paste = new Button { Content = "Paste", IsEnabled = _clipboard?.Table == _schema.Name };
        paste.Click += (_, _) => Paste(id);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var action in Actions)
        {
            var button = new Button { Content = action.Label };
            ToolTip.SetTip(button, action.Tip);
            button.Click += (_, _) =>
            {
                foreach (var (field, value) in action.Values)
                    _project.Set(_schema, id, field, value);
                _status($"{Label(id).Replace(" •", "")}: {action.Label}");
                RefreshLabel(id);
                ShowRecord();
            };
            buttons.Children.Add(button);
        }
        buttons.Children.Add(copy);
        buttons.Children.Add(paste);
        buttons.Children.Add(revert);
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(new TextBlock
        {
            Text = Label(id).Replace(" •", ""),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _fields.Children.Add(header);
        // The same thing in other tables, e.g. an item's weapon data and shop list.
        var related = AllTables.Related(_schema, id).Where(t => _project.HasTable(t.Name)).ToList();
        if (related.Count > 0)
        {
            var also = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            also.Children.Add(new TextBlock { Text = "Also:", Opacity = 0.7, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            foreach (var table in related)
                also.Children.Add(Link($"{table.Name} ›", () => _navigate(table.Name, id), new Thickness(0, 0, 14, 0)));
            _fields.Children.Add(also);
        }
        // Everything pointing at this record, e.g. the items using an item attributes entry.
        var users = UsersOf(id);
        if (users.Count > 0)
        {
            const int Shown = 15;
            var used = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            used.Children.Add(new TextBlock { Text = "Used by:", Opacity = 0.7, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            foreach (var (table, record, text) in users.Take(Shown))
                used.Children.Add(Link(text, () => _navigate(table.Name, record), new Thickness(0, 0, 12, 0)));
            if (users.Count > Shown)
                used.Children.Add(new TextBlock { Text = $"+{users.Count - Shown} more", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            _fields.Children.Add(used);
        }
        if (FieldNotes.For(_schema.Name) is { } tableNote)
            _fields.Children.Add(new TextBlock { Text = tableNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6) });

        if (_project.TextTableFor(_schema) is { } textTable && id < textTable.Rows)
            foreach (var field in textTable.Fields)
                _fields.Children.Add(TextRow(id, textTable, field.Name));
        foreach (var field in _schema.Fields)
            _fields.Children.Add(FieldRow(id, field));
        _building = false;
    }

    // A name or description, changed in the game when the mod is played or injected.
    private Control TextRow(int id, Core.Text.TextTable table, string field)
    {
        var changed = _project.IsTextChanged(table, id, field);
        var label = new TextBlock
        {
            Text = Humanize(field),
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = changed ? FontWeight.Bold : FontWeight.Normal,
        };
        if (changed)
            label.Foreground = ChangedBrush;
        ToolTip.SetTip(label, $"Original: {_project.OriginalText(table, id, field)}\nIn mods (tables.json): Text / {table.Name} / {field}");
        var multiline = field == "Description";
        var box = new TextBox
        {
            Text = _project.CurrentText(table, id, field) ?? "",
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 60 : 0,
            MinWidth = 260,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        void Save()
        {
            var value = box.Text ?? "";
            if (_building || value == (_project.CurrentText(table, id, field) ?? ""))
                return;
            _project.SetText(table, id, field, value);
            _status($"{table.Name} 0x{id:X2} {field} = {value}");
            RefreshLabel(id);
            Avalonia.Threading.Dispatcher.UIThread.Post(ShowRecord);
        }
        box.LostFocus += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && !multiline)
                Save();
        };
        var row = new DockPanel();
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(box);
        return row;
    }

    private Control FieldRow(int id, FieldDef field)
    {
        // Some fields mean something else depending on the record (a weapon's InflictStatus is a spell under formula 02).
        field = field.For(other => _project.Current(_schema, id, other));
        var changed = _project.IsChanged(_schema, id, field.Name);
        var label = new TextBlock
        {
            Text = field.Label ?? Humanize(field.Name),
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = changed ? FontWeight.Bold : FontWeight.Normal,
        };
        if (changed)
            label.Foreground = ChangedBrush;
        var original = $"Original: {Show(field, _project.Original(_schema, id, field.Name))}\nIn mods (tables.json): {field.Name}";
        ToolTip.SetTip(label, FieldNotes.For(_schema.Name, field.Name) is { } note ? $"{note}\n\n{original}" : original);
        if (field.LinksTo is { } linked)
        {
            // Click the label to edit the record this field points at.
            label.TextDecorations = TextDecorations.Underline;
            label.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            label.PointerPressed += (_, _) => _navigate(linked, int.Parse(_project.Current(_schema, id, field.Name)));
        }

        var row = new DockPanel();
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(Editor(id, field, label));
        return row;
    }

    private Button? _revert;

    // Records in any table whose linked field points at this record.
    private List<(TableSchema Table, int Record, string Text)> UsersOf(int id)
    {
        var users = new List<(TableSchema, int, string)>();
        // Not battle units: hundreds of them use the common jobs and items.
        foreach (var table in AllTables.List.Where(t => _project.HasTable(t.Name) && t != Battles.Units))
        {
            var links = table.Fields.Where(f => f.LinksTo is not null || f.When is not null).ToList();
            if (links.Count == 0)
                continue;
            for (var record = table.FirstId; record < table.FirstId + table.RecordCount; record++)
            {
                var current = record;
                if (!links.Any(f => f.For(other => _project.Current(table, current, other)) is { LinksTo: { } to }
                        && int.Parse(_project.Current(table, current, f.Name)) is var value && value == id
                        && AllTables.Resolve(to, value) == _schema))
                    continue;
                var name = table.NameOf(record);
                var sameKind = table.RecordNames is not null && table.RecordNames == Items.Common.RecordNames;
                users.Add((table, record, name is not null && sameKind ? name : $"{table.Name} {record:X2}{(name is null ? "" : " " + name)}"));
            }
        }
        // Skip "users" that are just records saying nothing (e.g. empty skill set slots pointing at ability 0).
        return id == 0 && _schema.RecordNames == Abilities.Common.RecordNames ? [] : users;
    }

    /// <summary>"HPConstant" as "HP Constant", "HairAdornment" as "Hair Adornment".</summary>
    public static string Humanize(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z0-9])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

    private static TextBlock Link(string text, Action go, Thickness margin)
    {
        var link = new TextBlock
        {
            Text = text,
            TextDecorations = TextDecorations.Underline,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Center,
        };
        link.PointerPressed += (_, _) => go();
        return link;
    }

    private static string Show(FieldDef field, string value) =>
        field.Kind == FieldKind.Flags ? (value.Length == 0 ? "(none)" : value) : value;

    private Control Editor(int id, FieldDef field, TextBlock label)
    {
        var current = _project.Current(_schema, id, field.Name);
        var list = field.Kind == FieldKind.AbilityId ? Names.Abilities : field.NameList;

        if (field.Kind == FieldKind.Flags)
        {
            var set = current.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var panel = new WrapPanel();
            foreach (var name in (field.FlagNames ?? []).OfType<string>())
            {
                var box = new CheckBox { Content = Humanize(name), Tag = name, IsChecked = set.Contains(name), Margin = new Thickness(0, 0, 12, 0) };
                if (FieldNotes.For(_schema.Name, field.Name, name) is { } note)
                {
                    ToolTip.SetTip(box, note);
                    box.FontStyle = FontStyle.Italic;
                }
                box.IsCheckedChanged += (_, _) =>
                {
                    var on = panel.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag!);
                    Commit(id, field, string.Join(", ", on));
                };
                panel.Children.Add(box);
            }
            // Tick or clear every named flag at once (e.g. sell an item in every shop).
            var named = (field.FlagNames ?? []).OfType<string>().ToList();
            var all = new Button { Content = "All", Padding = new Thickness(8, 2), FontSize = 12 };
            all.Click += (_, _) => Commit(id, field, string.Join(", ", named));
            var clear = new Button { Content = "Clear", Padding = new Thickness(8, 2), FontSize = 12 };
            clear.Click += (_, _) => Commit(id, field, "");
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            actions.Children.Add(all);
            actions.Children.Add(clear);
            var row = new DockPanel();
            DockPanel.SetDock(actions, Dock.Left);
            row.Children.Add(actions);
            row.Children.Add(panel);
            return row;
        }

        if (list is null && field.LinksTo is { } target && AllTables.ByName.TryGetValue(target, out var linkedTable))
        {
            // Just the entry numbers; what an entry holds is one click away.
            var ids = Enumerable.Range(linkedTable.FirstId, linkedTable.RecordCount).ToList();
            var at = ids.IndexOf(int.Parse(current));
            if (at >= 0)
            {
                var combo = new ComboBox { ItemsSource = ids.Select(i => $"{i:X2}").ToList(), SelectedIndex = at, MinWidth = 120, MaxDropDownHeight = 400 };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedIndex >= 0)
                        Commit(id, field, ids[combo.SelectedIndex].ToString());
                };
                return combo;
            }
        }

        if (list is not null)
        {
            // Name lists cover ids up to 0x1FF (abilities, effects); a nibble only 0-15.
            var max = Math.Min(Limit(field), 0x1FF);
            if (list == Names.InflictStatuses)
                max = Statuses.Inflict.RecordCount - 1;
            string? Name(int i) => field.Specials?.GetValueOrDefault(i) ?? NameIn(list, i);
            var items = Enumerable.Range(0, max + 1).Select(i => max > 0xFF ? $"{i:X3}  {Name(i) ?? ""}" : $"{i:X2}  {Name(i) ?? ""}").ToList();
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

        var limit = Limit(field);
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
        // Saved as typed, without redrawing the record: a redraw would replace this
        // box mid-number and drop the focus (and the next keystrokes).
        number.ValueChanged += (_, _) =>
        {
            if (_building || number.Value is not { } v)
                return;
            try
            {
                _project.Set(_schema, id, field.Name, ((int)v).ToString());
                _status($"{_schema.Name} 0x{id:X2} {field.Name} = {(int)v}");
            }
            catch (FormatException error)
            {
                _status(error.Message);
            }
            var changed = _project.IsChanged(_schema, id, field.Name);
            label.FontWeight = changed ? FontWeight.Bold : FontWeight.Normal;
            if (changed)
                label.Foreground = ChangedBrush;
            else
                label.ClearValue(TextBlock.ForegroundProperty);
            if (_revert is not null)
                _revert.IsEnabled = _project.IsChanged(_schema, id);
            RefreshLabel(id);
        };
        return number;
    }

    private string? NameIn(string list, int id) => list == Names.InflictStatuses ? InflictName(id) : Names.Get(list, id);

    // An inflict-status entry as the mod currently has it, e.g. "Poison (Cancel)".
    private string? InflictName(int id)
    {
        if (id == 0 || !_project.HasTable(Statuses.Inflict.Name))
            return id == 0 ? "None" : null;
        var statuses = _project.Current(Statuses.Inflict, id, "Statuses");
        var flags = _project.Current(Statuses.Inflict, id, "Flags");
        var text = statuses.Length == 0 ? "(no statuses)" : statuses;
        return flags.Length == 0 ? text : $"{text} ({flags})";
    }

    // The items using an item attributes entry, e.g. "Runeblade" or "Dagger, Mythril Knife +180 more".
    private string? AttributeUsers(int id)
    {
        var users = new List<string>();
        foreach (var (schema, first, count) in new[] { (Items.Common, 0, 0x100), (Items.Extra, 0x100, 5) })
            if (_project.HasTable(schema.Name))
                for (var item = first; item < first + count; item++)
                    if (_project.Current(schema, item, "SIA") == id.ToString())
                        users.Add(Names.Get(Names.Items, item) ?? $"item {item:X2}");
        return users.Count switch
        {
            0 => "(unused)",
            <= 3 => string.Join(", ", users),
            _ => $"{users[0]}, {users[1]} +{users.Count - 2} more",
        };
    }

    private void Paste(int id)
    {
        if (_clipboard is not { } copied || copied.Table != _schema.Name)
            return;
        var failed = 0;
        foreach (var (field, value) in copied.Values.Where(v => !PasteKeeps.Contains(v.Key)))
        {
            try
            {
                _project.Set(_schema, id, field, value);
            }
            catch (FormatException)
            {
                failed++;
            }
        }
        _status(failed == 0 ? $"Pasted onto {Label(id).Replace(" •", "")}{(PasteKeeps.Count > 0 ? $" (kept its own {string.Join(", ", PasteKeeps.Select(Humanize))})" : "")}" : $"Pasted, but {failed} field(s) couldn't be set");
        RefreshLabel(id);
        ShowRecord();
    }

    private static int Limit(FieldDef field) => field.Kind switch
    {
        FieldKind.Byte => 0xFF,
        FieldKind.UInt16 => 0xFFFF,
        FieldKind.Bits => field.Mask >> System.Numerics.BitOperations.TrailingZeroCount(field.Mask),
        _ => 0x1FF,
    };

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
