using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SwitchYard.App.ViewModels;

namespace SwitchYard.App.Views;

public partial class MainWindow : Window
{
    private const double CarouselItemWidth = 200 + 2 * 22; // cover + horizontal margins
    private bool _closeConfirmed;
    private DispatcherTimer? _scrollAnimation;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var list in new[] { CoverList, CarouselList })
        {
            // Enter or double-click on a cover = Play. Tunnel so the ListBox can't swallow Enter.
            list.AddHandler(KeyDownEvent, CoverList_KeyDown, RoutingStrategies.Tunnel);
            list.DoubleTapped += (_, _) => Play();
        }
        CarouselList.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() => CenterCarousel(animate: true), DispatcherPriority.Loaded);
        CarouselList.SizeChanged += (_, _) => { UpdateCarouselPadding(); CenterCarousel(animate: false); };
        KeyDown += Window_KeyDown;
        Closing += OnClosing;
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm) vm.PropertyChanged += Vm_PropertyChanged;
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>The cover list currently on screen (grid or carousel).</summary>
    public ListBox ActiveCoverList => Vm is { IsCarousel: true } ? CarouselList : CoverList;

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsCarousel))
        {
            // Switching views keeps the selection; move focus and scroll to it in the new view.
            Dispatcher.UIThread.Post(() =>
            {
                UpdateCarouselPadding();
                CenterCarousel(animate: false);
                FocusSelectedCover();
            }, DispatcherPriority.Loaded);
        }
    }

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

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsFocused)
        {
            FocusSelectedCover();
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

    /// <summary>Focus the selected cover in whichever view is showing.</summary>
    public void FocusSelectedCover()
    {
        var list = ActiveCoverList;
        if (list.SelectedItem is { } item)
            list.ScrollIntoView(item);
        if (list.ContainerFromIndex(list.SelectedIndex) is { } container)
            container.Focus(NavigationMethod.Directional);
        else
            list.Focus();
        if (list == CarouselList)
            CenterCarousel(animate: false);
    }

    // ------------------------------------------------------------------ carousel
    private ScrollViewer? CarouselScroller => CarouselList.FindDescendantOfType<ScrollViewer>();

    /// <summary>Side padding of half the viewport lets the first and last covers sit in the centre too.</summary>
    private void UpdateCarouselPadding()
    {
        var width = CarouselList.Bounds.Width;
        if (width <= 0) return;
        var side = Math.Max(0, width / 2 - CarouselItemWidth / 2);
        CarouselList.Padding = new Thickness(side, 0, side, 0);
    }

    /// <summary>Scroll so the selected cover sits in the middle of the row.</summary>
    public void CenterCarousel(bool animate)
    {
        if (!CarouselList.IsEffectivelyVisible || CarouselScroller is not { } sv) return;
        if (CarouselList.ContainerFromIndex(CarouselList.SelectedIndex) is not Control container) return;
        if (container.TranslatePoint(new Point(container.Bounds.Width / 2, 0), sv) is not { } center) return;

        var max = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        var target = Math.Clamp(sv.Offset.X + center.X - sv.Viewport.Width / 2, 0, max);
        _scrollAnimation?.Stop();
        if (!animate || Math.Abs(target - sv.Offset.X) < 1)
        {
            sv.Offset = new Vector(target, sv.Offset.Y);
            return;
        }

        // Ease-out glide over ~160 ms, matching the cover scale transition.
        var start = sv.Offset.X;
        var began = DateTime.UtcNow;
        _scrollAnimation = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - began).TotalMilliseconds / 160);
            var eased = 1 - Math.Pow(1 - t, 3);
            sv.Offset = new Vector(start + (target - start) * eased, sv.Offset.Y);
            if (t >= 1) _scrollAnimation?.Stop();
        });
        _scrollAnimation.Start();
    }
}
