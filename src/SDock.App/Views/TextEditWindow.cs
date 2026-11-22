using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SDock.App.Views;

/// <summary>Multi-line text editor dialog. Returns the text, or null if cancelled.</summary>
public sealed class TextEditWindow : Window
{
    private TextBox? _box;

    /// <summary>The on-screen keyboard (shown when the controller is in use).</summary>
    public Controls.OnScreenKeyboard Keyboard { get; }

    public void ShowKeyboard(bool show)
    {
        Keyboard.IsVisible = show;
        Height = show ? Math.Max(Height, 760) : 440;
        _box?.Focus();
    }

    public TextEditWindow() : this("Edit", "", "") { }

    public TextEditWindow(string title, string explanation, string initial)
    {
        Title = title;
        Width = 760;
        Height = 440;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var box = new TextBox { Text = initial, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = "Save", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
        save.Classes.Add("accent");
        save.Click += (_, _) => Close(box.Text ?? "");
        var cancel = new Button { Content = "Cancel", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        // The on-screen keyboard, for the controller: shown when the controller is in use, or with the Keyboard button.
        Keyboard = new Controls.OnScreenKeyboard { Target = box, Margin = new Thickness(0, 10, 0, 0), IsVisible = false };
        Keyboard.Done += () => Close(box.Text ?? "");
        Keyboard.Closed += () => ShowKeyboard(false);
        var keyboardButton = new Button { Content = "⌨ Keyboard", Focusable = false };
        keyboardButton.Classes.Add("subtle");
        keyboardButton.Click += (_, _) => ShowKeyboard(!Keyboard.IsVisible);

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(save);
        right.Children.Add(cancel);
        Grid.SetColumn(right, 2);
        buttons.Children.Add(keyboardButton);
        buttons.Children.Add(right);
        DockPanel.SetDock(Keyboard, Dock.Bottom);
        var note = new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        note.Classes.Add("muted");

        var dock = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(note);
        dock.Children.Add(Keyboard);
        dock.Children.Add(buttons);
        dock.Children.Add(box);
        Content = dock;
        _box = box;
        Opened += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = (box.Text ?? "").Length;
            if (Input.GamepadService.ControllerInUse) ShowKeyboard(true);
        };
    }
}
