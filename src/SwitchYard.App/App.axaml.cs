using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            var library = new LibraryStore(AppPaths.DatabasePath);
            var window = new MainWindow();
            var ui = new UiServices(window);
            var launcher = new EmulatorLauncher(Path.Combine(AppPaths.LogDir, "last_launch.log"));
            window.DataContext = new MainViewModel(library, launcher, ui);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                launcher.Dispose();
                library.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
