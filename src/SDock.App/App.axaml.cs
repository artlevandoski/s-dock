using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;

namespace SDock.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Everything lives in the S-Dock folder. If Windows won't let S-Dock write there (e.g. it was put in
            // Program Files), say so and stop, rather than scattering files elsewhere.
            if (AppPaths.IsPortable && !AppPaths.RootWritable)
            {
                var notice = new Views.MessageWindow("S-Dock", "S-Dock can't save in its folder",
                    $"S-Dock keeps everything (your library, artwork and settings) inside its own folder:\n\n{AppPaths.PortableRoot}\n\n" +
                    "Windows doesn't allow changes there. Move the whole S-Dock folder somewhere you can save files, such as " +
                    "your Documents folder or another drive, and start it again.", ["Close"]);
                notice.Closed += (_, _) => desktop.Shutdown();
                desktop.MainWindow = notice;
                base.OnFrameworkInitializationCompleted();
                return;
            }
            // Relative emulator paths always mean "inside S-Dock's folder", even with --data-dir.
            var library = new LibraryStore(AppPaths.DatabasePath, AppPaths.PortableRoot);
            if (AppPaths.IsPortable)
                _ = AppPaths.EmulatorsDir; // create emulators\ so it's obvious where builds go
            var window = new MainWindow();
            // Decode covers at the size they're drawn: the carousel's enlarged square cover (~354 px) times the display scaling.
            var scaling = window.Screens.Primary?.Scaling ?? 1.0;
            Controls.ImageCache.Covers.DecodeWidth = (int)Math.Ceiling(Math.Clamp(354 * scaling, 300, 600));
            var ui = new UiServices(window);
            var launcher = new EmulatorLauncher(Path.Combine(AppPaths.LogDir, "last_launch.log"));
            var vm = new MainViewModel(library, launcher, ui);
            window.DataContext = vm;
            desktop.MainWindow = window;
            window.Opened += (_, _) =>
            {
                if (window.WindowState == Avalonia.Controls.WindowState.FullScreen) WindowFocus.Bring(window);
                window.FocusSelectedCover();
            };
            if (!Program.Benchmark)
                window.Opened += async (_, _) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(4)); // let S-Dock settle (and any first-start questions) first
                    await vm.CheckForUpdatesOnStartAsync();
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
                    $"S-Dock hit an unexpected error and kept running, but the last action may not have finished.\n\n" +
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
