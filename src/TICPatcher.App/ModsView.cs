using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TICPatcher.Core.Mods;

namespace TICPatcher.App;

/// <summary>
/// The Mods tab: every mod installed in Reloaded-II for The Ivalice Chronicles,
/// turned on or off with a checkbox (saved in Reloaded-II's settings). Mods made
/// with TIC Patcher can be opened in the editor.
/// </summary>
public sealed class ModsView : UserControl
{
    private readonly Action<string> _status;
    private readonly Action<ReloadedSetup.InstalledMod> _edit;
    private readonly StackPanel _list = new() { Spacing = 8 };

    public ModsView(Action<string> status, Action<ReloadedSetup.InstalledMod> edit)
    {
        _status = status;
        _edit = edit;
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += (_, _) => Fill();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(refresh, Dock.Right);
        top.Children.Add(refresh);
        top.Children.Add(new TextBlock
        {
            Text = "Mods for The Ivalice Chronicles in Reloaded-II. Ticked mods load when you press Play. Load order and other settings stay as Reloaded-II has them.",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(new ScrollViewer { Content = _list });
        Content = root;
        Fill();
    }

    public void Fill()
    {
        _list.Children.Clear();
        if (ReloadedSetup.Find(out var problem) is not { } reloaded)
        {
            _list.Children.Add(new TextBlock { Text = problem, TextWrapping = TextWrapping.Wrap });
            return;
        }
        var mods = reloaded.GameMods();
        if (mods.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "No mods for the game yet. Make one in the other tabs and press Play.", TextWrapping = TextWrapping.Wrap });
            return;
        }
        var enabled = reloaded.EnabledMods().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
            _list.Children.Add(Row(reloaded, mod, enabled.Contains(mod.Id)));
    }

    private Control Row(ReloadedSetup reloaded, ReloadedSetup.InstalledMod mod, bool on)
    {
        var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Top };
        box.IsCheckedChanged += (_, _) =>
        {
            if (Process.GetProcessesByName("Reloaded-II").Length > 0)
            {
                _status("Close Reloaded-II's window first, so it doesn't undo the change.");
                Fill();
                return;
            }
            reloaded.SetEnabled(mod.Id, box.IsChecked == true);
            _status($"{mod.Name} turned {(box.IsChecked == true ? "on" : "off")}.");
        };

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = mod.Version.Length > 0 ? $"{mod.Name}  {mod.Version}" : mod.Name,
            FontWeight = FontWeight.SemiBold,
        });
        var detail = string.Join("  ·  ", new[] { mod.Author, mod.Id }.Where(t => t.Length > 0));
        text.Children.Add(new TextBlock { Text = detail, Opacity = 0.6, FontSize = 12 });
        if (mod.Description.Length > 0)
            text.Children.Add(new TextBlock { Text = mod.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });

        var row = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        if (mod.HasTables)
        {
            var edit = new Button { Content = "Edit", VerticalAlignment = VerticalAlignment.Top };
            ToolTip.SetTip(edit, "Open this mod's table changes in the editor.");
            edit.Click += (_, _) => _edit(mod);
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(edit);
        }
        row.Children.Add(text);
        return new Border { Child = row, Padding = new Thickness(8), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
    }
}
