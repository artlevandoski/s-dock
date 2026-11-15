using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class MainWindow : Window
{
    private const double CarouselItemWidth = 300 + 2 * 3 + 2 * 18; // cover + ring border + horizontal margins
    private bool _closeConfirmed;
    private DispatcherTimer? _scrollAnimation;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var list in new[] { CoverList, CarouselList })
        {
            // Enter or double-click on a cover = Play. Tunnel so the ListBox can't swallow Enter.
            list.AddHandler(KeyDownEvent, CoverList_KeyDown, RoutingStrategies.Tunnel);
            list.DoubleTapped += (_, _) => Play();
        }
        foreach (var list in new[] { CoverList, CarouselList })
            list.ContextRequested += Cover_ContextRequested; // right-click (or the menu key) opens Game Actions
        MenuList.AddHandler(KeyDownEvent, MenuList_KeyDown, RoutingStrategies.Tunnel);
        MenuList.Tapped += (_, e) =>
        {
            if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is MenuEntry entry)
                Vm?.ActivateMenu(entry);
        };
        MenuOverlay.PointerPressed += (_, e) =>
        {
            if (e.Source == MenuOverlay) Vm?.CloseMenu(); // a click outside the panel closes it
        };
        CarouselList.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() => CenterCarousel(animate: true), DispatcherPriority.Loaded);
        CarouselList.SizeChanged += (_, _) => { UpdateCarouselPadding(); CenterCarousel(animate: false); };
        KeyDown += Window_KeyDown;
        Closing += OnClosing;
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
                vm.MenuChanged += OnMenuChanged;
                vm.QuitRequested += () => Close();
                ApplyFullScreen(vm.IsCarousel);
            }
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>The cover list currently on screen (grid or carousel).</summary>
    public ListBox ActiveCoverList => Vm is { IsCarousel: true } ? CarouselList : CoverList;

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsCarousel))
        {
            ApplyFullScreen(Vm!.IsCarousel);
            // Switching views keeps the selection; move focus and scroll to it in the new view.
            Dispatcher.UIThread.Post(() =>
            {
                UpdateCarouselPadding();
                CenterCarousel(animate: false);
                FocusSelectedCover();
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Carousel view fills the screen; grid view puts the window back the way it was.</summary>
    private void ApplyFullScreen(bool fullScreen)
    {
        if (fullScreen && WindowState != WindowState.FullScreen)
        {
            _stateBeforeFullScreen = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            WindowState = WindowState.FullScreen;
            if (IsVisible)
                Dispatcher.UIThread.Post(() => { WindowFocus.Bring(this); FocusSelectedCover(); }, DispatcherPriority.Loaded);
        }
        else if (!fullScreen && WindowState == WindowState.FullScreen)
        {
            WindowState = _stateBeforeFullScreen;
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
        // Letters: Up/Down in the carousel (it only scrolls sideways), Page Up/Down in either view.
        else if ((sender == CarouselList && e.Key is Key.Up or Key.Down) || e.Key is Key.PageUp or Key.PageDown)
        {
            JumpLetter(e.Key is Key.Up or Key.PageUp ? -1 : +1);
            e.Handled = true;
        }
    }

    private DispatcherTimer? _letterTimer;

    /// <summary>Jump to the next or previous letter and show it briefly.</summary>
    public void JumpLetter(int direction)
    {
        if (Vm is not { } vm || vm.JumpToLetter(direction) is null) return;
        FocusSelectedCover();
        _letterTimer?.Stop();
        _letterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(650), DispatcherPriority.Normal, (_, _) =>
        {
            vm.ShowJumpLetter = false;
            _letterTimer?.Stop();
        });
        _letterTimer.Start();
    }

    // ------------------------------------------------------------------ on-screen menu
    private void Cover_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm is not { } vm) return;
        e.Handled = true;
        if (vm.IsCarousel) return; // Game Actions are off in the carousel for now
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is EditionItem item)
            vm.SelectedItem = item;
        vm.OpenGameMenu();
        e.Handled = true;
    }

    private void OnMenuChanged()
    {
        if (Vm is { IsMenuOpen: true })
            Dispatcher.UIThread.Post(FocusMenu, DispatcherPriority.Loaded);
        else
            Dispatcher.UIThread.Post(FocusSelectedCover, DispatcherPriority.Loaded);
    }

    /// <summary>Put focus on the menu's selected line (the first one that can be chosen, when a page opens).</summary>
    public void FocusMenu()
    {
        if (Vm is not { IsMenuOpen: true } vm) return;
        if (MenuList.SelectedItem is not MenuEntry selected || !vm.MenuItems.Contains(selected))
            MenuList.SelectedItem = vm.MenuItems.FirstOrDefault(m => m.Enabled) ?? vm.MenuItems.FirstOrDefault();
        if (MenuList.SelectedIndex >= 0)
        {
            MenuList.ScrollIntoView(MenuList.SelectedIndex);
            if (MenuList.ContainerFromIndex(MenuList.SelectedIndex) is { } container)
            {
                if (!container.IsFocused) container.Focus(NavigationMethod.Directional);
                return;
            }
        }
        MenuList.Focus();
    }

    private void MenuList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        var selected = MenuList.SelectedItem as MenuEntry;
        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                vm.ActivateMenu(selected);
                break;
            case Key.Right when selected is { HasPage: true }:
                vm.ActivateMenu(selected);
                break;
            case Key.Escape or Key.Back:
            case Key.Left when vm.MenuHasParent:
                vm.MenuBack();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            // Search lives in the grid: from the carousel, switch first, then focus the box once it's shown.
            if (Vm is { IsCarousel: true } vm) vm.IsCarousel = false;
            Dispatcher.UIThread.Post(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, DispatcherPriority.Loaded);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Vm is { IsMenuOpen: true } menuVm)
        {
            menuVm.MenuBack();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsFocused)
        {
            FocusSelectedCover();
            e.Handled = true;
        }
    }


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

    // ------------------------------------------------------------------ benchmark (--benchmark)
    private Task NextFrameAsync()
    {
        var tcs = new TaskCompletionSource();
        RequestAnimationFrame(_ => tcs.TrySetResult());
        return tcs.Task;
    }

    private static string Summarize(string what, List<double> frames)
    {
        if (frames.Count == 0) return $"{what}: no frames";
        var sorted = frames.Order().ToList();
        var p95 = sorted[(int)(sorted.Count * 0.95) - 1];
        return $"{what,-34} avg {frames.Average(),6:0.0} ms   p95 {p95,6:0.0} ms   max {sorted[^1],6:0.0} ms   " +
               $"slow (>25 ms) {frames.Count(f => f > 25),3} of {frames.Count}";
    }

    /// <summary>
    /// Measures real frame times with the actual renderer: waits for the covers, scrolls the grid, then steps
    /// through the carousel one game per frame. Returns a short report.
    /// </summary>
    public async Task<string> RunBenchmarkAsync()
    {
        var vm = Vm!;
        var report = new System.Text.StringBuilder();
        await NextFrameAsync();
        report.AppendLine($"Launch to first frame {Program.Started.ElapsedMilliseconds} ms");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Controls.ImageCache.Covers.Pending > 0 && sw.Elapsed < TimeSpan.FromSeconds(60))
            await Task.Delay(50);
        report.AppendLine($"Games {vm.Items.Count}; covers decoded {Controls.ImageCache.Covers.Count} in {sw.ElapsedMilliseconds} ms " +
                          $"({Controls.ImageCache.Covers.Bytes / 1048576} MB at {Controls.ImageCache.Covers.DecodeWidth} px); " +
                          $"display scaling {RenderScaling:0.##}");

        async Task<List<double>> Measure(int count, Action step)
        {
            var frames = new List<double>();
            await NextFrameAsync();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < count; i++)
            {
                step();
                await NextFrameAsync();
                frames.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart();
            }
            return frames;
        }

        vm.IsCarousel = false;
        for (var i = 0; i < 10; i++) await NextFrameAsync();
        var scroller = CoverList.FindDescendantOfType<ScrollViewer>()!;
        report.AppendLine(Summarize("Grid: smooth scroll (40 px/frame)", await Measure(150, () =>
            scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + 40))));
        report.AppendLine(Summarize("Grid: page jumps (700 px/frame)", await Measure(60, () =>
            scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + 700))));

        vm.IsCarousel = true;
        for (var i = 0; i < 20; i++) await NextFrameAsync();
        report.AppendLine(Summarize("Carousel: one game per frame", await Measure(120, () =>
            CarouselList.SelectedIndex = Math.Min(CarouselList.ItemCount - 1, CarouselList.SelectedIndex + 1))));
        report.AppendLine(Summarize("Carousel: idle frames", await Measure(60, () => { })));

        report.AppendLine($"Process working set {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576} MB");
        return report.ToString();
    }

    /// <summary>Opens the Game Actions menu (controller X).</summary>
    public void OpenGameActions() => Vm?.OpenGameMenu();

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
        if (!CarouselList.IsEffectivelyVisible || CarouselScroller is not { } sv || CarouselList.SelectedIndex < 0) return;

        // The side padding depends on the current width; make sure it (and the scroll extent) is up to date first.
        var padding = CarouselList.Padding;
        UpdateCarouselPadding();
        if (CarouselList.Padding != padding)
            CarouselList.UpdateLayout();

        // The carousel only creates controls for covers near the screen and estimates the rest, so centre on
        // the selected cover's real position. If it hasn't been created yet (a far jump), bring it into view
        // first, which creates it, then centre exactly on the next pass.
        if (CarouselList.ContainerFromIndex(CarouselList.SelectedIndex) is not Control container ||
            container.TranslatePoint(new Point(container.Bounds.Width / 2, 0), sv) is not { } point)
        {
            CarouselList.ScrollIntoView(CarouselList.SelectedIndex);
            if (CarouselList.ContainerFromIndex(CarouselList.SelectedIndex) is null) return;
            Dispatcher.UIThread.Post(() => CenterCarousel(animate: false), DispatcherPriority.Loaded);
            return;
        }
        var max = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        var target = Math.Clamp(sv.Offset.X + point.X - sv.Viewport.Width / 2, 0, max);
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
