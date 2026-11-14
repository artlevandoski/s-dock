namespace SDock.App.Input;

/// <summary>XInput button bits.</summary>
[Flags]
public enum PadButtons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    /// <summary>Not XInput bits: the analog triggers pulled past halfway (see <see cref="GamepadMapper"/>).</summary>
    LeftTrigger = 0x0400,
    RightTrigger = 0x0800,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public readonly record struct PadState(PadButtons Buttons, short LeftX = 0, short LeftY = 0, byte LeftTrigger = 0, byte RightTrigger = 0)
{
    public const byte TriggerThreshold = 128;

    /// <summary>The buttons, with the triggers counted as pressed past halfway.</summary>
    public PadButtons AllButtons => Buttons
        | (LeftTrigger >= TriggerThreshold ? PadButtons.LeftTrigger : 0)
        | (RightTrigger >= TriggerThreshold ? PadButtons.RightTrigger : 0);
}

public enum PadAction
{
    Up,
    Down,
    Left,
    Right,
    /// <summary>A: activate (Play on a cover, press the focused button in dialogs).</summary>
    Accept,
    /// <summary>B: back / cancel (closes dialogs).</summary>
    Cancel,
    /// <summary>LB.</summary>
    PreviousLibrary,
    /// <summary>RB.</summary>
    NextLibrary,
    /// <summary>Y: switch grid / carousel.</summary>
    ToggleView,
    /// <summary>X: open the Game Actions menu.</summary>
    GameActions,
    /// <summary>Start: Play.</summary>
    Play,
    /// <summary>Select / Back: the Settings menu.</summary>
    Menu,
    /// <summary>LT: jump to the previous letter.</summary>
    PreviousLetter,
    /// <summary>RT: jump to the next letter.</summary>
    NextLetter,
}

/// <summary>
/// Turns raw controller state into navigation actions: button presses fire once, and a held
/// direction (D-pad or left stick) repeats after a short delay, like a held arrow key.
/// </summary>
public sealed class GamepadMapper
{
    public const short StickDeadzone = 16000; // ~half travel, so a resting or drifting stick does nothing
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(110);

    private static readonly (PadButtons Button, PadAction Action)[] ButtonActions =
    [
        (PadButtons.A, PadAction.Accept),
        (PadButtons.B, PadAction.Cancel),
        (PadButtons.LeftShoulder, PadAction.PreviousLibrary),
        (PadButtons.RightShoulder, PadAction.NextLibrary),
        (PadButtons.Y, PadAction.ToggleView),
        (PadButtons.X, PadAction.GameActions),
        (PadButtons.Start, PadAction.Play),
        (PadButtons.Back, PadAction.Menu),
        (PadButtons.LeftTrigger, PadAction.PreviousLetter),
        (PadButtons.RightTrigger, PadAction.NextLetter),
    ];

    private PadButtons _previous;
    private PadAction? _heldDirection;
    private TimeSpan _nextRepeat;

    /// <summary>Forget held buttons (e.g. when the window loses focus) so nothing fires on return.</summary>
    public void Reset(PadState? current = null)
    {
        _previous = current?.AllButtons ?? PadButtons.None;
        _heldDirection = current is { } c ? Direction(c) : null;
        _nextRepeat = TimeSpan.MaxValue; // a direction held through a reset doesn't fire until released
    }

    public static PadAction? Direction(PadState s)
    {
        if (s.Buttons.HasFlag(PadButtons.DpadUp)) return PadAction.Up;
        if (s.Buttons.HasFlag(PadButtons.DpadDown)) return PadAction.Down;
        if (s.Buttons.HasFlag(PadButtons.DpadLeft)) return PadAction.Left;
        if (s.Buttons.HasFlag(PadButtons.DpadRight)) return PadAction.Right;
        int x = s.LeftX, y = s.LeftY;
        if (Math.Max(Math.Abs(x), Math.Abs(y)) < StickDeadzone) return null;
        return Math.Abs(x) >= Math.Abs(y)
            ? (x > 0 ? PadAction.Right : PadAction.Left)
            : (y > 0 ? PadAction.Up : PadAction.Down); // XInput: +Y is up
    }

    public List<PadAction> Update(PadState state, TimeSpan now)
    {
        var actions = new List<PadAction>();

        var buttons = state.AllButtons;
        var pressed = buttons & ~_previous;
        foreach (var (button, action) in ButtonActions)
            if (pressed.HasFlag(button))
                actions.Add(action);
        _previous = buttons;

        var direction = Direction(state);
        if (direction != _heldDirection)
        {
            _heldDirection = direction;
            if (direction is { } d)
            {
                actions.Add(d);
                _nextRepeat = now + RepeatDelay;
            }
        }
        else if (direction is { } d && now >= _nextRepeat)
        {
            actions.Add(d);
            _nextRepeat = now + RepeatInterval;
        }
        return actions;
    }
}
