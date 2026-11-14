using Avalonia.Controls;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class ProfilePickerWindow : Window
{
    public ProfilePickerWindow() => InitializeComponent();

    public ProfilePickerWindow(ProfilePickerViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += ok => Close(ok);
        // Double-click a profile to play with it.
        ProfileList.DoubleTapped += (_, _) =>
        {
            if (vm.PlayCommand.CanExecute(null)) vm.PlayCommand.Execute(null);
        };
    }
}
