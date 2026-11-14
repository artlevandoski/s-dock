using Avalonia.Controls;
using SwitchYard.App.ViewModels;

namespace SwitchYard.App.Views;

public partial class ImportPreviewWindow : Window
{
    public ImportPreviewWindow() => InitializeComponent();

    public ImportPreviewWindow(ImportPreviewViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += ok => Close(ok);
    }
}
