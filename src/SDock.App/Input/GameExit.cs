using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SDock.App.ViewModels;

namespace SDock.App.Input;

/// <summary>
/// Decides whether an exit press (Esc, or Start+Select) should end the game: straight away in one-press mode,
/// or on a second press within <see cref="Window"/> in two-press mode.
/// </summary>
public sealed class ExitPressGate
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    private TimeSpan? _armedUntil;

    public bool IsArmed(TimeSpan now) => _armedUntil is { } until && now <= until;

    /// <summary>Returns true when the game should exit now; false when this press only armed it.</summary>
    public bool Press(TimeSpan now, bool twoPresses)
    {
        if (!twoPresses || IsArmed(now))
        {
            _armedUntil = null;
            return true;
        }
        _armedUntil = now + Window;
        return false;
    }

    public void Reset() => _armedUntil = null;
}

/// <summary>
/// While a game runs, Esc (when the emulator is in front) and Start+Select on the controller close the game and
/// its emulator and bring S-Dock back. Two-press mode shows a small "press again" note over the game first.
/// </summary>
public sealed class GameExitService : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly ExitPressGate _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private EscapeKeyHook? _hook;
    private ToastWindow? _toast;

    public GameExitService(MainViewModel vm)
    {
        _vm = vm;
        vm.Launcher.Launched += (_, pid) =>
        {
            _gate.Reset();
            _hook?.Dispose();
            _hook = OperatingSystem.IsWindows() ? EscapeKeyHook.TryInstall(pid, Press) : null;
        };
        vm.Launcher.Finished += (_, _, _) => Stopped();
        vm.Launcher.Failed += (_, _) => Stopped();
    }

    /// <summary>An exit press from the keyboard or controller.</summary>
    public void Press()
    {
        if (!_vm.Launcher.IsRunning) return;
        if (_gate.Press(_clock.Elapsed, _vm.ExitWithTwoPresses))
        {
            HideToast();
            _vm.ExitGame();
        }
        else
        {
            ShowToast("Press again to exit the game");
        }
    }

    private void Stopped()
    {
        _hook?.Dispose();
        _hook = null;
        _gate.Reset();
        HideToast();
    }

    private void ShowToast(string text)
    {
        _toast ??= new ToastWindow();
        _toast.Show(text, ExitPressGate.Window);
    }

    private void HideToast() => _toast?.Hide();

    public void Dispose()
    {
        _hook?.Dispose();
        _toast?.Close();
    }
}

/// <summary>A small note shown over the game without taking focus from it.</summary>
internal sealed class ToastWindow : Window
{
    private readonly TextBlock _text;
    private IDisposable? _hideTimer;
    private const double ToastWidth = 440, ToastHeight = 64;

    public ToastWindow()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        Focusable = false;
        Width = ToastWidth;
        Height = ToastHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E6171829")),
            BorderBrush = new SolidColorBrush(Color.Parse("#C3A6FF")),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Child = _text,
        };
    }

    public void Show(string text, TimeSpan duration)
    {
        _text.Text = text;
        if (Screens.Primary is { } screen)
        {
            var area = screen.Bounds; // over a full-screen game, so the whole screen rather than the work area
            var scale = screen.Scaling;
            Position = new PixelPoint(area.X + (int)((area.Width - ToastWidth * scale) / 2),
                                      area.Y + area.Height - (int)((ToastHeight + 80) * scale));
        }
        if (!IsVisible) Show();
        _hideTimer?.Dispose();
        _hideTimer = DispatcherTimer.RunOnce(Hide, duration);
    }
}

/// <summary>
/// Watches for Esc system-wide while the emulator is the window in front, and takes the key for S-Dock
/// (the emulator doesn't see it). Any other app in front gets Esc as usual.
/// </summary>
internal sealed class EscapeKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13, WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint VkEscape = 0x1B;

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc fn, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    private readonly int _pid;
    private readonly Action _onEscape;
    private readonly LowLevelKeyboardProc _proc; // kept alive for as long as the hook is installed
    private IntPtr _hook;
    private bool _down;

    private EscapeKeyHook(int pid, Action onEscape)
    {
        _pid = pid;
        _onEscape = onEscape;
        _proc = Callback;
    }

    public static EscapeKeyHook? TryInstall(int pid, Action onEscape)
    {
        var hook = new EscapeKeyHook(pid, onEscape);
        hook._hook = SetWindowsHookExW(WhKeyboardLl, hook._proc, GetModuleHandleW(null), 0);
        return hook._hook == IntPtr.Zero ? null : hook;
    }

    private bool EmulatorInFront()
    {
        var window = GetForegroundWindow();
        return window != IntPtr.Zero && GetWindowThreadProcessId(window, out var pid) != 0 && pid == _pid;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        // Runs for every key press on the machine: keep it quick and hand the real work to the UI thread.
        if (code >= 0 && (uint)Marshal.ReadInt32(lParam) == VkEscape)
        {
            var msg = (int)wParam;
            if (msg is WmKeyUp or WmSysKeyUp)
            {
                var wasDown = _down;
                _down = false;
                if (wasDown) return 1;
            }
            else if (msg is WmKeyDown or WmSysKeyDown && (_down || EmulatorInFront()))
            {
                if (!_down) Dispatcher.UIThread.Post(_onEscape); // held keys repeat; act on the first press only
                _down = true;
                return 1;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
