using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SDock.App.ViewModels;
using SDock.App.Views;

namespace SDock.App.Input;

/// <summary>Reads Xbox-compatible controllers through Windows XInput (no extra drivers or packages).</summary>
internal static class XInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState14(uint index, out XInputState state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState910(uint index, out XInputState state);

    private static int _api; // 0 = untried, 1 = xinput1_4, 2 = xinput9_1_0, -1 = unavailable

    public static bool Available => _api >= 0;

    /// <summary>Reads controller <paramref name="index"/> (0–3). Null when disconnected or XInput is missing.</summary>
    public static PadState? Read(int index)
    {
        if (_api < 0 || !OperatingSystem.IsWindows()) return null;
        XInputState s;
        uint result;
        try
        {
            if (_api is 0 or 1)
            {
                try { result = GetState14((uint)index, out s); _api = 1; }
                catch (DllNotFoundException) when (_api == 0) { result = GetState910((uint)index, out s); _api = 2; }
            }
            else
            {
                result = GetState910((uint)index, out s);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _api = -1;
            return null;
        }
        return result == 0
            ? new PadState((PadButtons)s.Gamepad.Buttons, s.Gamepad.ThumbLX, s.Gamepad.ThumbLY, s.Gamepad.LeftTrigger, s.Gamepad.RightTrigger)
            : null;
    }
}

/// <summary>
/// Polls the controller while one of S-Dock's windows is in front, and turns presses into the same
/// keys the keyboard uses (arrows, Enter, Escape) plus a few main-window shortcuts. While another app
/// (such as the emulator) is in front, input is ignored, except Start+Select, which leaves a running game.
/// </summary>
public sealed class GamepadService
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly GamepadMapper _mapper = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private int? _connectedIndex;
    private TimeSpan _nextScan;
    private bool _wasActive;
    private bool _exitComboHeld;

    /// <summary>Receives Start+Select (Back) presses while a game runs.</summary>
    public GameExitService? GameExit { get; set; }

    public GamepadService(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Poll());
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private PadState? ReadController()
    {
        var now = _clock.Elapsed;
        if (_connectedIndex is { } i && XInput.Read(i) is { } state)
            return state;
        _connectedIndex = null;
        // Polling empty slots is comparatively slow, so look for a newly connected controller only every 2 s.
        if (now < _nextScan) return null;
        _nextScan = now + TimeSpan.FromSeconds(2);
        for (var slot = 0; slot < 4 && XInput.Available; slot++)
            if (XInput.Read(slot) is { } found)
            {
                _connectedIndex = slot;
                return found;
            }
        return null;
    }

    private void Poll()
    {
        var state = ReadController();
        // Start+Select works over the emulator too: it's how you leave a game.
        var combo = state is { } st && (st.Buttons & (PadButtons.Start | PadButtons.Back)) == (PadButtons.Start | PadButtons.Back);
        if (combo && !_exitComboHeld)
            GameExit?.Press();
        _exitComboHeld = combo;
        var active = _desktop.Windows.FirstOrDefault(w => w.IsActive);
        if (active is null && state is { Buttons: not PadButtons.None } && _desktop.MainWindow is MainWindow
            {
                WindowState: WindowState.FullScreen, DataContext: MainViewModel { IsRunning: false },
            } main && WindowFocus.NothingInFront())
        {
            // Full screen with nothing else in front (e.g. Windows didn't hand focus back after a game):
            // a button press brings S-Dock back instead of being ignored.
            WindowFocus.Bring(main);
            main.FocusSelectedCover();
        }
        if (state is not { } s || active is null)
        {
            _wasActive = false;
            return;
        }
        if (!_wasActive)
        {
            // Coming back to S-Dock (e.g. after the emulator closes): ignore buttons already held.
            _mapper.Reset(s);
            _wasActive = true;
            return;
        }
        foreach (var action in _mapper.Update(s, _clock.Elapsed))
            Dispatch(active, action);
    }

    public static void Dispatch(Window active, PadAction action)
    {
        if (active is MainWindow { DataContext: MainViewModel { IsMenuOpen: true } menuVm } menuWindow)
        {
            // The on-screen menu is open: X and Select close it; the D-pad, A and B work inside it (below).
            switch (action)
            {
                case PadAction.GameActions or PadAction.Menu: menuVm.CloseMenu(); return;
                case PadAction.PreviousLibrary or PadAction.NextLibrary or PadAction.ToggleView or PadAction.Play
                    or PadAction.PreviousLetter or PadAction.NextLetter: return;
            }
            menuWindow.FocusMenu();
        }
        else if (active is MainWindow { DataContext: MainViewModel vm } main)
        {
            switch (action)
            {
                case PadAction.PreviousLibrary: vm.PreviousLibraryCommand.Execute(null); main.FocusSelectedCover(); return;
                case PadAction.NextLibrary: vm.NextLibraryCommand.Execute(null); main.FocusSelectedCover(); return;
                case PadAction.ToggleView: vm.ToggleViewCommand.Execute(null); return;
                case PadAction.GameActions: main.OpenGameActions(); return;
                case PadAction.Play: if (vm.CanPlay) vm.PlayCommand.Execute(null); return;
                case PadAction.Menu: vm.OpenSettingsMenu(); return;
                case PadAction.PreviousLetter: main.JumpLetter(-1); return;
                case PadAction.NextLetter: main.JumpLetter(+1); return;
            }
            // Nothing focused yet (e.g. right after start): put focus on the selected cover first.
            if (TopLevel.GetTopLevel(main)?.FocusManager?.GetFocusedElement() is null)
                main.FocusSelectedCover();
        }
        var key = action switch
        {
            PadAction.Up => Key.Up,
            PadAction.Down => Key.Down,
            PadAction.Left => Key.Left,
            PadAction.Right => Key.Right,
            PadAction.Accept => Key.Enter,
            PadAction.Cancel => Key.Escape,
            _ => Key.None,
        };
        if (key != Key.None)
            SendKey(active, key);
    }

    /// <summary>Delivers a key press to the focused control, exactly as if it came from the keyboard.</summary>
    public static void SendKey(Window window, Key key)
    {
        var target = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Interactive ?? window;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = target });
    }
}
