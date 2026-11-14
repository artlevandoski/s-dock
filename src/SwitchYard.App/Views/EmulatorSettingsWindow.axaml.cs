using Avalonia.Controls;
using SwitchYard.App.ViewModels;

namespace SwitchYard.App.Views;

public partial class EmulatorSettingsWindow : Window
{
    public EmulatorSettingsWindow() => InitializeComponent();

    public EmulatorSettingsWindow(EmulatorProfilesViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += ok => Close(ok);
    }
}
