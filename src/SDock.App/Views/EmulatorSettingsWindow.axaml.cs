using Avalonia.Controls;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class EmulatorSettingsWindow : Window
{
    public EmulatorSettingsWindow() => InitializeComponent();

    public EmulatorSettingsWindow(EmulatorProfilesViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += ok => Close(ok);
    }
}
