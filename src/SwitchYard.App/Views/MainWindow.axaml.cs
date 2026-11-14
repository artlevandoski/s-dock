using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SwitchYard.App.ViewModels;

namespace SwitchYard.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        // Enter or double-click on a cover = Play. Tunnel so the ListBox can't swallow Enter.
        CoverList.AddHandler(KeyDownEvent, CoverList_KeyDown, RoutingStrategies.Tunnel);
        CoverList.DoubleTapped += (_, _) => Play();
        Closing += OnClosing;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Play()
    {
        if (Vm is { CanPlay: true } vm)
            vm.PlayCommand.Execute(null);
    }

    private void CoverList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Play();
            e.Handled = true;
        }
    }

    private void Quit_Click(object? sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || Vm is not { Launcher.IsRunning: true } vm)
            return;
        e.Cancel = true;
        if (await vm.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>Focus the selected cover in the grid.</summary>
    public void FocusSelectedCover()
    {
        if (CoverList.SelectedItem is { } item)
            CoverList.ScrollIntoView(item);
        if (CoverList.ContainerFromIndex(CoverList.SelectedIndex) is { } container)
            container.Focus(NavigationMethod.Directional);
        else
            CoverList.Focus();
    }
}
