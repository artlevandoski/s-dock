using Avalonia.Controls;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class IgdbSettingsWindow : Window
{
    public IgdbSettingsWindow() => InitializeComponent();

    public IgdbSettingsWindow(IgdbSettingsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += ok => Close(ok);
    }
}
