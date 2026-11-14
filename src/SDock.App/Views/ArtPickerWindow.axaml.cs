using Avalonia.Controls;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class ArtPickerWindow : Window
{
    public ArtPickerWindow() => InitializeComponent();

    public ArtPickerWindow(ArtPickerViewModel vm) : this()
    {
        DataContext = vm;
        Title = vm.IsLogo ? "Choose Logo" : "Choose Background";
        vm.CloseRequested += pick => Close(pick);
        ChoiceList.DoubleTapped += (_, _) =>
        {
            if (vm.ChooseCommand.CanExecute(null)) vm.ChooseCommand.Execute(null);
        };
        Opened += async (_, _) => await vm.StartAsync();
    }
}
