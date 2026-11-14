using Avalonia.Controls;
using SwitchYard.App.ViewModels;

namespace SwitchYard.App.Views;

public partial class VersionsWindow : Window
{
    public VersionsWindow() => InitializeComponent();

    public VersionsWindow(VersionsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += path => Close(path);
        VersionList.DoubleTapped += (_, _) =>
        {
            if (vm.PlayCommand.CanExecute(null)) vm.PlayCommand.Execute(null);
        };
    }
}
