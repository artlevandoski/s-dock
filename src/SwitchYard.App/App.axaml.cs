using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SwitchYard.App.ViewModels;
using SwitchYard.App.Views;
using SwitchYard.Core;

namespace SwitchYard.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Portable: data\ beside the app. On first portable start, bring the earlier library across.
            string? copiedFrom = null;
            if (AppPaths.IsPortable)
            {
                try { copiedFrom = AppPaths.CopyLegacyLibrary(AppPaths.DataDir, AppPaths.UserProfileDir); }
                catch (IOException ex) { LogError(ex); }
            }
            // Relative emulator paths always mean "inside Switch Yard's folder", even with --data-dir.
            var library = new LibraryStore(AppPaths.DatabasePath, AppPaths.PortableRoot);
            if (AppPaths.IsPortable)
            {
                _ = AppPaths.EmulatorsDir; // create emulators\ so it's obvious where builds go
                if (copiedFrom is not null)
                    foreach (var p in library.ListProfiles())
                        library.SaveProfile(p); // emulators inside the folder are now stored relative to it
            }
            var window = new MainWindow();
            // Decode covers at the size they're drawn: the carousel's enlarged square cover (~354 px) times the display scaling.
            var scaling = window.Screens.Primary?.Scaling ?? 1.0;
            Controls.ImageCache.Covers.DecodeWidth = (int)Math.Ceiling(Math.Clamp(354 * scaling, 300, 600));
            var ui = new UiServices(window);
            var launcher = new EmulatorLauncher(Path.Combine(AppPaths.LogDir, "last_launch.log"));
            var vm = new MainViewModel(library, launcher, ui);
            if (copiedFrom is not null)
                vm.StatusText = $"Your library was copied into {AppPaths.DataDir}. The original is still at {copiedFrom}.";
            else if (!AppPaths.IsPortable && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AppPaths.DataDirEnv)))
                vm.StatusText = $"Switch Yard's folder is read-only, so data is kept in {AppPaths.DataDir}.";
            window.DataContext = vm;
            desktop.MainWindow = window;
            window.Opened += (_, _) =>
            {
                if (window.WindowState == Avalonia.Controls.WindowState.FullScreen) WindowFocus.Bring(window);
                window.FocusSelectedCover();
            };
            if (Program.Benchmark)
            {
                // --benchmark: measure real frame times on this machine, write logs\benchmark.txt, then exit.
                window.Opened += async (_, _) =>
                {
                    var report = await window.RunBenchmarkAsync();
                    File.WriteAllText(Path.Combine(AppPaths.LogDir, "benchmark.txt"), report);
                    desktop.Shutdown();
                };
            }

            var gameExit = new Input.GameExitService(vm);
            var gamepad = new Input.GamepadService(desktop) { GameExit = gameExit };
            gamepad.Start();
            desktop.Exit += (_, _) =>
            {
                gamepad.Stop();
                gameExit.Dispose();
            };

            // Safety net: log unexpected errors and keep the app running instead of closing it.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                var logPath = LogError(e.Exception);
                e.Handled = true;
                _ = ui.ShowMessageAsync("Something went wrong",
                    $"Switch Yard hit an unexpected error and kept running, but the last action may not have finished.\n\n" +
                    $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\nDetails were saved to:\n{logPath}");
            };
            desktop.Exit += (_, _) =>
            {
                launcher.Dispose();
                library.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Appends the error to logs\errors.log and returns the log path.</summary>
    public static string LogError(Exception ex)
    {
        var path = Path.Combine(AppPaths.LogDir, "errors.log");
        try
        {
            File.AppendAllText(path, $"==== {LibraryStore.UtcNow()}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }
        return path;
    }
}
