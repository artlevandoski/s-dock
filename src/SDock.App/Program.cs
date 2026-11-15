using Avalonia;
using SDock.Core;

namespace SDock.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The installer (Velopack) runs the app briefly during install, update and uninstall; this handles those
        // runs and returns straight away for a normal start. It must come first.
        Velopack.VelopackApp.Build().Run();

        // --data-dir <folder>: use a separate library instead of the one in S-Dock's data folder (for testing).
        int i = Array.IndexOf(args, "--data-dir");
        if (i >= 0 && i + 1 < args.Length)
            Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, Path.GetFullPath(args[i + 1]));

        Benchmark = args.Contains("--benchmark");
        Started.Start();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>--benchmark: measure frame times with the real renderer and exit (for performance checks).</summary>
    public static bool Benchmark { get; private set; }

    /// <summary>Time since launch (for the benchmark's startup figure).</summary>
    public static readonly System.Diagnostics.Stopwatch Started = new();

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
