using Avalonia.Controls;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class ArtPickerWindow : Window
{
    public ArtPickerWindow() => InitializeComponent();

    public ArtPickerWindow(ArtPickerViewModel vm) : this()
    {
        DataContext = vm;
        Title = vm.Kind switch
        {
            SDock.Core.Metadata.ArtKind.Logo => "Choose Logo",
            SDock.Core.Metadata.ArtKind.Cover => "Choose Cover",
            _ => "Choose Background",
        };
        vm.CloseRequested += pick => Close(pick);
        ChoiceList.DoubleTapped += (_, _) =>
        {
            if (vm.ChooseCommand.CanExecute(null)) vm.ChooseCommand.Execute(null);
        };
        Opened += async (_, _) =>
        {
            await vm.StartAsync();
            // The images arrive after the window opens: give the list the focus so the D-pad picks at once.
            if (ChoiceList.ItemCount > 0 && ChoiceList.ContainerFromIndex(Math.Max(0, ChoiceList.SelectedIndex)) is { } item)
                item.Focus(Avalonia.Input.NavigationMethod.Directional);
        };
    }
}
