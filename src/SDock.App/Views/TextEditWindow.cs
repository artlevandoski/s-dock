using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SDock.App.Views;

/// <summary>Multi-line text editor dialog. Returns the text, or null if cancelled.</summary>
public sealed class TextEditWindow : Window
{
    public TextEditWindow() : this("Edit", "", "") { }

    public TextEditWindow(string title, string explanation, string initial)
    {
        Title = title;
        Width = 620;
        Height = 440;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var box = new TextBox { Text = initial, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = "Save", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        save.Classes.Add("accent");
        save.Click += (_, _) => Close(box.Text ?? "");
        var cancel = new Button { Content = "Cancel", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var note = new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        note.Classes.Add("muted");

        var dock = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(note);
        dock.Children.Add(buttons);
        dock.Children.Add(box);
        Content = dock;
        Opened += (_, _) => box.Focus();
    }
}
