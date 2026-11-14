using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;

namespace SDock.App.Views;

/// <summary>
/// Bringing S-Dock to the front for real. Windows only lets an app take the foreground if it received the
/// last input, so after the emulator closes (or when going full screen) a plain Activate() often just flashes the
/// taskbar, and the controller is ignored until you click the window. This works around that the way game
/// launchers do: borrow the foreground's input queue, and tap Alt if Windows still refuses.
/// </summary>
public static class WindowFocus
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int max);

    private const byte VkMenu = 0x12;
    private const uint KeyEventFKeyUp = 0x0002;

    /// <summary>Activates the window and makes sure Windows actually puts it in front.</summary>
    public static void Bring(Window window)
    {
        window.Activate();
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
            return;
        try
        {
            if (GetForegroundWindow() == hwnd) return;
            var foreground = GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
            var thisThread = GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != thisThread &&
                           AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached) AttachThreadInput(thisThread, foregroundThread, false);
            }
            if (GetForegroundWindow() != hwnd)
            {
                // A synthetic Alt tap counts as input from this app, which lifts the foreground lock.
                keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
                keybd_event(VkMenu, 0, KeyEventFKeyUp, UIntPtr.Zero);
                SetForegroundWindow(hwnd);
            }
            window.Activate();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>
    /// True when nothing that matters is in front: no window, or only the desktop or taskbar (what's left
    /// after an emulator closes). Then the controller may bring S-Dock back without stealing from an app.
    /// </summary>
    public static bool NothingInFront()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return true;
            var name = new StringBuilder(64);
            GetClassNameW(foreground, name, name.Capacity);
            return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
