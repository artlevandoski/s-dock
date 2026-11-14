using Avalonia;
using SwitchYard.Core;

namespace SwitchYard.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // --data-dir <folder>: use a separate library instead of %LOCALAPPDATA%\SwitchYard.
        int i = Array.IndexOf(args, "--data-dir");
        if (i >= 0 && i + 1 < args.Length)
            Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, Path.GetFullPath(args[i + 1]));

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
