using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SDock.App.Input;

namespace SDock.App.Controls;

/// <summary>
/// A keyboard on screen for typing with a controller (or clicking). It types into <see cref="Target"/>, which keeps
/// the focus and caret. Controller: D-pad moves, A types, B deletes (and closes when there's nothing to delete),
/// X space, Y shift, LB/RB move the cursor, Select switches to symbols, Start is Done.
/// </summary>
public sealed class OnScreenKeyboard : UserControl
{
    private const int Columns = 10;

    private sealed record Key(string Label, string? Text, int Column, int Span, Action<OnScreenKeyboard>? Run = null);

    private static readonly string[] LetterRows = ["1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm,.-"];
    private static readonly string[] SymbolRows = ["1234567890", "!?&:;()/#+", "\"@%*=_~^$€", "<>[]{}|\\™®"];

    private readonly Grid _grid = new() { RowSpacing = 6, ColumnSpacing = 6 };
    private readonly List<List<(Key Key, Button Button)>> _rows = [];
    private bool _shift, _symbols;
    private int _row = 1, _col;

    /// <summary>The text box being typed into.</summary>
    public TextBox? Target { get; set; }

    /// <summary>Start or the Done key.</summary>
    public event Action? Done;
    /// <summary>B with nothing left to delete.</summary>
    public event Action? Closed;

    public OnScreenKeyboard()
    {
        Focusable = false;
        for (var c = 0; c < Columns; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var r = 0; r < 5; r++) _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var hints = new TextBlock
        {
            Text = "A type   B delete   X space   Y shift   LB/RB move cursor   Select symbols   Start done",
            FontSize = 13, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
        };
        var panel = new StackPanel();
        panel.Children.Add(_grid);
        panel.Children.Add(hints);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F2171829")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3AFFFFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Child = panel,
        };
        Build();
    }

    /// <summary>The key the D-pad is on (for tests).</summary>
    public string HighlightedKey => _rows[_row][_col].Key.Label;

    private void Build()
    {
        _grid.Children.Clear();
        _rows.Clear();
        var rows = _symbols ? SymbolRows : LetterRows;
        for (var r = 0; r < rows.Length; r++)
        {
            var row = new List<(Key, Button)>();
            for (var c = 0; c < rows[r].Length; c++)
            {
                var ch = rows[r][c].ToString();
                var text = _shift && !_symbols ? ch.ToUpperInvariant() : ch;
                row.Add(Add(new Key(text, text, c, 1), r));
            }
            _rows.Add(row);
        }
        _rows.Add(
        [
            Add(new Key(_shift ? "⇧ SHIFT" : "⇧ Shift", null, 0, 2, k => k.ToggleShift()), 4),
            Add(new Key(_symbols ? "ABC" : "&123", null, 2, 2, k => k.ToggleSymbols()), 4),
            Add(new Key("Space", " ", 4, 3), 4),
            Add(new Key("⌫", null, 7, 1, k => k.Backspace()), 4),
            Add(new Key("Done", null, 8, 2, k => k.Done?.Invoke()), 4),
        ]);
        _row = Math.Min(_row, _rows.Count - 1);
        _col = Math.Min(_col, _rows[_row].Count - 1);
        Highlight();
    }

    private (Key, Button) Add(Key key, int row)
    {
        var button = new Button
        {
            Content = key.Label, Focusable = false, MinHeight = 46, FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add("subtle");
        button.Click += (_, _) => Press(key);
        Grid.SetRow(button, row);
        Grid.SetColumn(button, key.Column);
        Grid.SetColumnSpan(button, key.Span);
        _grid.Children.Add(button);
        return (key, button);
    }

    private void Highlight()
    {
        for (var r = 0; r < _rows.Count; r++)
            for (var c = 0; c < _rows[r].Count; c++)
            {
                var b = _rows[r][c].Button;
                var hot = r == _row && c == _col;
                b.Classes.Set("accent", hot);
                b.Classes.Set("subtle", !hot);
            }
    }

    private void Press(Key key)
    {
        if (key.Run is { } run) run(this);
        else if (key.Text is { } text)
        {
            Type(text);
            if (_shift && !_symbols) { _shift = false; Build(); } // one capital, like a phone keyboard
        }
    }

    // ----------------------------------------------------------------- editing the target
    public void Type(string text)
    {
        if (Target is not { } box) return;
        var current = box.Text ?? "";
        var (start, length) = Selection(box, current);
        box.Text = current.Remove(start, length).Insert(start, text);
        box.CaretIndex = start + text.Length;
        box.SelectionStart = box.SelectionEnd = box.CaretIndex;
    }

    /// <summary>Deletes the selection or the character before the cursor. False when there was nothing to delete.</summary>
    public bool Backspace()
    {
        if (Target is not { } box) return false;
        var current = box.Text ?? "";
        var (start, length) = Selection(box, current);
        if (length == 0)
        {
            if (start == 0) return false;
            start--;
            length = 1;
        }
        box.Text = current.Remove(start, length);
        box.CaretIndex = start;
        box.SelectionStart = box.SelectionEnd = start;
        return true;
    }

    private static (int Start, int Length) Selection(TextBox box, string text)
    {
        var a = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        var b = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        return b > a ? (a, b - a) : (Math.Clamp(box.CaretIndex, 0, text.Length), 0);
    }

    private void MoveCaret(int by)
    {
        if (Target is not { } box) return;
        box.CaretIndex = Math.Clamp(box.CaretIndex + by, 0, (box.Text ?? "").Length);
        box.SelectionStart = box.SelectionEnd = box.CaretIndex;
    }

    private void ToggleShift() { _shift = !_shift; Build(); }
    private void ToggleSymbols() { _symbols = !_symbols; _shift = false; Build(); }

    // ----------------------------------------------------------------- controller
    /// <summary>A controller press. Returns true when the keyboard used it.</summary>
    public bool Input(PadAction action)
    {
        switch (action)
        {
            case PadAction.Left or PadAction.Right:
                var row = _rows[_row];
                _col = (_col + (action == PadAction.Right ? 1 : -1) + row.Count) % row.Count;
                Highlight();
                return true;
            case PadAction.Up or PadAction.Down:
                var center = _rows[_row][_col].Key.Column + _rows[_row][_col].Key.Span / 2.0;
                _row = (_row + (action == PadAction.Down ? 1 : -1) + _rows.Count) % _rows.Count;
                // The key in the new row under the same spot.
                var target = _rows[_row].FindIndex(k => center >= k.Key.Column && center < k.Key.Column + k.Key.Span);
                _col = target >= 0 ? target : Math.Min(_col, _rows[_row].Count - 1);
                Highlight();
                return true;
            case PadAction.Accept: Press(_rows[_row][_col].Key); return true;
            case PadAction.Cancel: if (!Backspace()) Closed?.Invoke(); return true;
            case PadAction.GameActions: Type(" "); return true;          // X
            case PadAction.ToggleView: ToggleShift(); return true;      // Y
            case PadAction.PreviousLibrary or PadAction.PreviousLetter: MoveCaret(-1); return true; // LB / LT
            case PadAction.NextLibrary or PadAction.NextLetter: MoveCaret(+1); return true;         // RB / RT
            case PadAction.Menu: ToggleSymbols(); return true;          // Select
            case PadAction.Play: Done?.Invoke(); return true;           // Start
        }
        return false;
    }

    /// <summary>The keyboard showing in this window, if any.</summary>
    public static OnScreenKeyboard? In(Window window) =>
        window.GetVisualDescendants().OfType<OnScreenKeyboard>().FirstOrDefault(k => k.IsEffectivelyVisible);
}
