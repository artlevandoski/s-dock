using Avalonia.Controls;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class ImportPreviewWindow : Window
{
    public ImportPreviewWindow() => InitializeComponent();

    public ImportPreviewWindow(ImportPreviewViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += ok => Close(ok);
    }
}
