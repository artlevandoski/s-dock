using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SDock.App.Input;

/// <summary>
/// The controller in dialogs (messages, choosers, pickers, text editors, settings windows):
/// the D-pad moves within a list and then between controls, A presses or chooses, B cancels.
/// Dialogs are built for mouse and keyboard; this fills the gaps so none of them traps the controller.
/// </summary>
public static class DialogNavigation
{
    /// <summary>Call when a dialog opens: focus its list (or main button) so the D-pad works at once.</summary>
    public static void Attach(Window dialog)
    {
        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (Focused(dialog) is null) FocusFirst(dialog);
        }, DispatcherPriority.Background);
    }

    private static Control? Focused(Window window) =>
        TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() is Control c && c.FindAncestorOfType<Window>(includeSelf: true) == window
            ? c
            : null;

    private static IEnumerable<T> All<T>(Window window) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled);

    /// <summary>A list with items first (its selected item), then the default button, then whatever comes first.</summary>
    public static void FocusFirst(Window window)
    {
        if (All<ListBox>(window).FirstOrDefault(l => l.ItemCount > 0) is { } list)
        {
            if (list.SelectedIndex < 0) list.SelectedIndex = 0;
            list.ScrollIntoView(list.SelectedIndex);
            if (list.ContainerFromIndex(list.SelectedIndex) is { } item && item.Focus(NavigationMethod.Directional)) return;
        }
        if (All<Button>(window).FirstOrDefault(b => b.IsDefault) is { } main && main.Focus(NavigationMethod.Directional)) return;
        if (All<Button>(window).FirstOrDefault() is { } first && first.Focus(NavigationMethod.Directional)) return;
        if (Stops(window).FirstOrDefault() is { } stop) FocusStop(stop);
    }

    public static void Handle(Window window, PadAction action)
    {
        // An open dropdown shows its list in a pop-up of its own, so look for it before anything else.
        if (All<ComboBox>(window).FirstOrDefault(c => c.IsDropDownOpen) is { } dropdown)
        {
            switch (action)
            {
                case PadAction.Up: dropdown.SelectedIndex = Math.Max(0, dropdown.SelectedIndex - 1); break;
                case PadAction.Down: dropdown.SelectedIndex = Math.Min(dropdown.ItemCount - 1, dropdown.SelectedIndex + 1); break;
                case PadAction.Accept or PadAction.Cancel: dropdown.IsDropDownOpen = false; dropdown.Focus(); break;
            }
            return;
        }
        var focused = Focused(window);
        if (focused is null)
        {
            FocusFirst(window);
            if (action is PadAction.Up or PadAction.Down or PadAction.Left or PadAction.Right) return; // that press only woke it up
            focused = Focused(window);
            if (focused is null) return;
        }

        switch (action)
        {
            case PadAction.Up or PadAction.Down or PadAction.Left or PadAction.Right:
                Move(window, focused, action);
                break;
            case PadAction.Accept:
                Accept(window, focused);
                break;
            case PadAction.Cancel:
                if (!Send(focused, Key.Escape))
                {
                    if (All<Button>(window).FirstOrDefault(b => b.IsCancel) is { } cancel) Press(cancel);
                    else window.Close();
                }
                break;
        }
    }

    private static void Move(Window window, Control focused, PadAction action)
    {
        var key = action switch { PadAction.Up => Key.Up, PadAction.Down => Key.Down, PadAction.Left => Key.Left, _ => Key.Right };
        // In a list, the D-pad moves between its items; past the first or last one it moves on to the next control.
        var list = focused.FindAncestorOfType<ListBox>(includeSelf: true);
        if (list is not null)
        {
            var before = list.SelectedIndex;
            Send(focused, key);
            if (list.SelectedIndex != before)
            {
                if (list.ContainerFromIndex(list.SelectedIndex) is { } item) item.Focus(NavigationMethod.Directional);
                return;
            }
        }
        else if (focused is Slider && Send(focused, key))
            return;

        // Buttons, text boxes and the like don't use the D-pad: it moves to the previous/next control.
        var stops = Stops(window);
        Control current = list ?? focused;
        var index = stops.IndexOf(current);
        if (index < 0) index = stops.FindIndex(c => current.GetVisualAncestors().Contains(c));
        var target = index + (action is PadAction.Down or PadAction.Right ? 1 : -1);
        if (index >= 0 && target >= 0 && target < stops.Count) FocusStop(stops[target]);
    }

    /// <summary>
    /// The controls the D-pad can land on, in the order they appear: buttons, check boxes, text boxes, dropdowns,
    /// and each list as a single stop.
    /// </summary>
    private static List<Control> Stops(Window window)
    {
        var stops = new List<Control>();
        foreach (var c in window.GetVisualDescendants().OfType<Control>())
        {
            if (!c.Focusable || !c.IsEffectivelyVisible || !c.IsEffectivelyEnabled || !KeyboardNavigation.GetIsTabStop(c)) continue;
            if (c is SelectableTextBlock or ScrollViewer or ListBoxItem or ComboBoxItem) continue;
            if (c.GetVisualAncestors().Any(a => a is ListBox or ComboBox || (a is Control ac && stops.Contains(ac) && ac is TextBox))) continue;
            stops.Add(c);
        }
        return stops
            .Select(c => (Control: c, At: c.TranslatePoint(new Avalonia.Point(0, 0), window) ?? new Avalonia.Point(0, 0)))
            .OrderBy(x => Math.Round(x.At.Y / 12))
            .ThenBy(x => x.At.X)
            .Select(x => x.Control)
            .ToList();
    }

    private static void FocusStop(Control stop)
    {
        if (stop is ListBox l && l.ItemCount > 0)
        {
            if (l.SelectedIndex < 0) l.SelectedIndex = 0;
            l.ScrollIntoView(l.SelectedIndex);
            if (l.ContainerFromIndex(l.SelectedIndex) is { } item && item.Focus(NavigationMethod.Directional)) return;
        }
        stop.Focus(NavigationMethod.Tab);
        stop.BringIntoView();
    }

    private static void Accept(Window window, Control focused)
    {
        switch (focused)
        {
            case ComboBox combo:
                combo.IsDropDownOpen = true;
                return;
            case RadioButton radio:
                radio.IsChecked = true;
                return;
            case ToggleButton toggle: // check boxes and toggle buttons
                toggle.IsChecked = !(toggle.IsChecked ?? false);
                return;
            case Button button:
                Press(button);
                return;
        }
        // A list item or a text box: confirm with the dialog's main button (typing needs a keyboard anyway).
        if (All<Button>(window).FirstOrDefault(b => b.IsDefault) is { } main) Press(main);
        else Send(focused, Key.Enter);
    }

    /// <summary>Clicks a button exactly as the mouse would (its Click handlers and Command both run).</summary>
    public static void Press(Button button)
    {
        if (Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(button) is Avalonia.Automation.Provider.IInvokeProvider invoke)
            invoke.Invoke();
        else
            Send(button, Key.Enter);
    }

    /// <summary>Raises a key press on a control. Returns true if something handled it.</summary>
    private static bool Send(Control target, Key key)
    {
        var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target };
        target.RaiseEvent(down);
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = target });
        return down.Handled;
    }
}
