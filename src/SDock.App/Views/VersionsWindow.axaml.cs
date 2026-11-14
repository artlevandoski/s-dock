using Avalonia.Controls;
using SDock.App.ViewModels;

namespace SDock.App.Views;

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
