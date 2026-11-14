using Avalonia.Controls;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class MatchWindow : Window
{
    public MatchWindow() => InitializeComponent();

    public MatchWindow(MatchViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += game => Close(game);
        ResultList.DoubleTapped += (_, _) =>
        {
            if (vm.MatchCommand.CanExecute(null)) vm.MatchCommand.Execute(null);
        };
        Opened += async (_, _) => await vm.StartAsync();
    }
}
