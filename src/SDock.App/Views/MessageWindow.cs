using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SDock.App.Views;

/// <summary>Simple message box with custom buttons. Returns the clicked label (or null if closed).</summary>
public sealed class MessageWindow : Window
{
    public MessageWindow() : this("S-Dock", null, "", ["OK"]) { }

    public MessageWindow(string title, string? heading, string message, IReadOnlyList<string> buttons)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        if (!string.IsNullOrEmpty(heading))
            panel.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < buttons.Count; i++)
        {
            var label = buttons[i];
            var b = new Button { Content = label, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == 0) { b.Classes.Add("accent"); b.IsDefault = true; }
            b.Click += (_, _) => Close(label);
            row.Children.Add(b);
        }
        panel.Children.Add(row);
        Content = panel;
    }
}
