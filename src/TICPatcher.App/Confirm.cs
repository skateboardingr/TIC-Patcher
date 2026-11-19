using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TICPatcher.App;

/// <summary>A small yes/no question in its own window.</summary>
public static class Confirm
{
    public static async Task<bool> Ask(Window owner, string title, string message, string yes)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = yes };
        var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return await dialog.ShowDialog<bool>(owner);
    }
}
