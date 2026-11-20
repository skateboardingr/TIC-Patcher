using Avalonia;
using Avalonia.Controls;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.App;

/// <summary>
/// Battles (ENTD), as FFTPatcher shows them: pick a battle, then edit its 16
/// unit slots in an ordinary table editor.
/// </summary>
public sealed class BattleView : UserControl
{
    private readonly ModProject _project;
    private readonly ListBox _battles = new();
    private readonly TextBox _filter = new() { PlaceholderText = "Filter battles", Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _changedOnly = new() { Content = "Changed only", Margin = new Thickness(0, 0, 0, 6) };
    private readonly List<int> _shown = [];
    private bool _stale;

    public TableEditor Units { get; }

    public BattleView(ModProject project, Action<string> status, Action<string, int> navigate)
    {
        _project = project;
        Units = new TableEditor(project, Battles.Units, status, navigate)
        {
            Describe = Describe,
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
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(left);
        Grid.SetColumn(Units, 1);
        grid.Children.Add(Units);
        Content = grid;

        _filter.TextChanged += (_, _) => Fill();
        _changedOnly.IsCheckedChanged += (_, _) => Fill();
        _battles.SelectionChanged += (_, _) =>
        {
            if (_battles.SelectedIndex >= 0 && _battles.SelectedIndex < _shown.Count)
                Units.ShowRange(_shown[_battles.SelectedIndex] * Battles.UnitsPerBattle, Battles.UnitsPerBattle);
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
