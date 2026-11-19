using SDL;
using static SDL.SDL3;

namespace SDock.App.Input;

/// <summary>
/// Reads every kind of controller through SDL3: Xbox, PlayStation (DualShock 4, DualSense), Switch Pro, Joy-Con
/// pairs, 8BitDo and other pads SDL knows. Buttons are mapped by their printed label, so the button marked A (or ✕)
/// always confirms and B (or ○) goes back, whatever its position. All connected controllers count, combined.
/// </summary>
public static unsafe class SdlPads
{
    private static int _state; // 0 = not started, 1 = running, -1 = unavailable (XInput is used instead)
    private static readonly Dictionary<SDL_JoystickID, IntPtr> _open = [];
    private static long _nextScan;

    public static bool Available => _state >= 0;

    /// <summary>Starts SDL's controller support. False when SDL3.dll can't be loaded or started.</summary>
    public static bool Start()
    {
        if (_state != 0) return _state > 0;
        try
        {
            // S-Dock keeps reading while the emulator is in front (Start + Select leaves a game).
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS"u8, "1"u8);
            // Leave controllers as they are, so the emulator sees them exactly as before: no switching PlayStation
            // controllers into their enhanced mode, no player lights.
            SDL_SetHint("SDL_JOYSTICK_ENHANCED_REPORTS"u8, "0"u8);
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5_PLAYER_LED"u8, "0"u8);
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH_PLAYER_LED"u8, "0"u8);
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_JOYCON_HOME_LED"u8, "0"u8);
            if (!SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD))
            {
                _state = -1;
                return false;
            }
            _state = 1;
            Scan();
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _state = -1;
            return false;
        }
    }

    /// <summary>The names of the connected controllers, for Settings.</summary>
    public static IReadOnlyList<string> Names
    {
        get
        {
            if (_state <= 0) return [];
            return _open.Values.Select(p => SDL_GetGamepadName((SDL_Gamepad*)p) ?? "Controller").ToList();
        }
    }

    /// <summary>All connected controllers combined into one state, or null when none is connected.</summary>
    public static PadState? Read()
    {
        if (_state <= 0) return null;
        SDL_UpdateGamepads();
        var now = Environment.TickCount64;
        if (now >= _nextScan || _open.Count == 0 && now >= _nextScan - 1500) Scan();
        PadState? combined = null;
        foreach (var (id, ptr) in _open.ToList())
        {
            var pad = (SDL_Gamepad*)ptr;
            if (!SDL_GamepadConnected(pad))
            {
                EventLog.Write("Controller disconnected");
                SDL_CloseGamepad(pad);
                _open.Remove(id);
                continue;
            }
            var state = Translate(
                b => SDL_GetGamepadButton(pad, b),
                b => SDL_GetGamepadButtonLabel(pad, b),
                a => SDL_GetGamepadAxis(pad, a));
            combined = combined is { } c ? Combine(c, state) : state;
        }
        return combined;
    }

    /// <summary>Opens newly connected controllers (every 2 seconds; SDL also notices unplugging itself).</summary>
    private static void Scan()
    {
        _nextScan = Environment.TickCount64 + 2000;
        int count;
        var ids = SDL_GetGamepads(&count);
        if (ids is null) return;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var id = ids[i];
                if (_open.ContainsKey(id)) continue;
                var pad = SDL_OpenGamepad(id);
                if (pad is not null)
                {
                    _open[id] = (IntPtr)pad;
                    EventLog.Write($"Controller connected: {SDL_GetGamepadName(pad)} ({SDL_GetGamepadType(pad)})");
                }
            }
        }
        finally
        {
            SDL_free(ids);
        }
    }

    /// <summary>
    /// One controller's state in S-Dock's terms (the XInput layout it has always used). Face buttons go by label:
    /// A / ✕ → A (confirm), B / ○ → B (back), X / □ → X, Y / △ → Y.
    /// </summary>
    public static PadState Translate(Func<SDL_GamepadButton, bool> pressed, Func<SDL_GamepadButton, SDL_GamepadButtonLabel> label,
        Func<SDL_GamepadAxis, short> axis)
    {
        var buttons = PadButtons.None;
        foreach (var face in (ReadOnlySpan<SDL_GamepadButton>)[SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST,
                     SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH])
        {
            if (!pressed(face)) continue;
            buttons |= label(face) switch
            {
                SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_A or SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_CROSS => PadButtons.A,
                SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_B or SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_CIRCLE => PadButtons.B,
                SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_X or SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_SQUARE => PadButtons.X,
                SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_Y or SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_TRIANGLE => PadButtons.Y,
                // No label known: by position, Xbox style.
                _ => face switch
                {
                    SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => PadButtons.A,
                    SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => PadButtons.B,
                    SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => PadButtons.X,
                    _ => PadButtons.Y,
                },
            };
        }
        void Map(SDL_GamepadButton b, PadButtons to) { if (pressed(b)) buttons |= to; }
        Map(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP, PadButtons.DpadUp);
        Map(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN, PadButtons.DpadDown);
        Map(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, PadButtons.DpadLeft);
        Map(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, PadButtons.DpadRight);
        Map(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, PadButtons.Start);  // Start, Options, +
        Map(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK, PadButtons.Back);    // Back/View, Share/Create, −
        Map(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER, PadButtons.LeftShoulder);
        Map(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER, PadButtons.RightShoulder);
        // SDL: Y is positive downwards and triggers run 0..32767; S-Dock (XInput): Y up, triggers 0..255.
        static byte Trigger(short v) => (byte)Math.Clamp(v / 128, 0, 255);
        var y = axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY);
        return new PadState(buttons,
            axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX),
            (short)Math.Clamp(-(int)y, short.MinValue, short.MaxValue),
            Trigger(axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER)),
            Trigger(axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER)));
    }

    /// <summary>Two controllers as one: buttons from either, the stick and triggers pushed furthest.</summary>
    public static PadState Combine(PadState a, PadState b)
    {
        static short Far(short x, short y) => Math.Abs((int)x) >= Math.Abs((int)y) ? x : y;
        return new PadState(a.Buttons | b.Buttons, Far(a.LeftX, b.LeftX), Far(a.LeftY, b.LeftY),
            Math.Max(a.LeftTrigger, b.LeftTrigger), Math.Max(a.RightTrigger, b.RightTrigger));
    }
}
