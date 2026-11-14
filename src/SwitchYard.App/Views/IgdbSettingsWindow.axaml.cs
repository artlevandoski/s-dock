using Avalonia.Controls;
using SwitchYard.App.ViewModels;

namespace SwitchYard.App.Views;

public partial class IgdbSettingsWindow : Window
{
    public IgdbSettingsWindow() => InitializeComponent();

    public IgdbSettingsWindow(IgdbSettingsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += ok => Close(ok);
    }
}
