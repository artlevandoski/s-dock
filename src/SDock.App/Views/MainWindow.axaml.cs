using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SDock.App.Input;
using SDock.App.ViewModels;

namespace SDock.App.Views;

public partial class MainWindow : Window
{
    private const double CarouselItemWidth = 300 + 2 * 3 + 2 * 18; // cover + ring border + horizontal margins
    private bool _closeConfirmed;
    private DispatcherTimer? _scrollAnimation;
    /// <summary>The grid opens maximized, including after starting in the carousel.</summary>
    private WindowState _stateBeforeFullScreen = WindowState.Maximized;

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
        FilterBar.AddHandler(KeyDownEvent, FilterBar_KeyDown, RoutingStrategies.Tunnel);
        // The Settings page takes every key while it's open, before anything under it sees them.
        AddHandler(KeyDownEvent, Settings_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, Trailer_KeyDown, RoutingStrategies.Tunnel);
        SettingsRowList.Tapped += (_, e) =>
        {
            if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return; // the ‹ › arrows
            if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is SettingRow row)
                _ = Vm?.ActivateSettingAsync(row);
        };
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
                vm.SettingsChanged += OnSettingsChanged;
                vm.TrailerScript += script => _ = _trailer?.RunAsync(script);
                vm.QuitRequested += () => Close();
                ApplyFullScreen(vm.IsCarousel);
            }
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>The cover list currently on screen (grid or carousel).</summary>
    public ListBox ActiveCoverList => Vm is { IsCarousel: true } ? CarouselList : CoverList;

    // ------------------------------------------------------------------ Settings page
    private void Settings_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { IsSettingsOpen: true } vm) return;
        PadAction? action = e.Key switch
        {
            Key.Up => PadAction.Up,
            Key.Down => PadAction.Down,
            Key.Left => PadAction.Left,
            Key.Right => PadAction.Right,
            Key.Enter or Key.Space => PadAction.Accept,
            Key.Escape or Key.Back => PadAction.Cancel,
            _ => null,
        };
        if (action is not { } a) return;
        vm.SettingsInput(a);
        e.Handled = true;
    }

    private void OnSettingsChanged()
    {
        if (Vm is { IsSettingsOpen: true })
            Dispatcher.UIThread.Post(() => SettingsPage.Focus(), DispatcherPriority.Loaded);
        else
            Dispatcher.UIThread.Post(FocusSelectedCover, DispatcherPriority.Loaded);
    }

    private void SettingPrevious_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SettingRow row) Vm?.StepSetting(row, -1);
    }

    private void SettingNext_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SettingRow row) Vm?.StepSetting(row, +1);
    }

    // ------------------------------------------------------------------ trailer
    private Controls.TrailerView? _trailer;

    /// <summary>The trailer player, once one has played (for the --trailer-test check).</summary>
    public Controls.TrailerView? TrailerPlayer => _trailer;

    private void UpdateTrailer()
    {
        if (Vm is not { } vm) return;
        if (vm.IsTrailerOpen && vm.TrailerVideoId is { } video)
        {
            if (_trailer is null && OperatingSystem.IsWindows() && TryGetPlatformHandle() is not null && MainViewModel.TrailerPlayerAvailable())
            {
                _trailer = new Controls.TrailerView();
                _trailer.CloseRequested += () => vm.CloseTrailer();
                _trailer.Failed += message => { vm.CloseTrailer(); vm.StatusText = $"The trailer couldn't play: {message}"; };
                TrailerHost.Child = _trailer;
            }
            _trailer?.Play(video);
        }
        else
        {
            _trailer?.Stop();
            Dispatcher.UIThread.Post(FocusSelectedCover, DispatcherPriority.Loaded);
        }
    }

    private void Trailer_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { IsTrailerOpen: true } vm) return;
        PadAction? action = e.Key switch
        {
            Key.Space or Key.Enter => PadAction.Accept,
            Key.Left => PadAction.Left,
            Key.Right => PadAction.Right,
            Key.Up => PadAction.Up,
            Key.Down => PadAction.Down,
            Key.Escape or Key.Back => PadAction.Cancel,
            _ => null,
        };
        if (action is { } a) vm.TrailerInput(a);
        e.Handled = true; // nothing under the trailer reacts while it plays
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsTrailerOpen) or nameof(MainViewModel.TrailerVideoId)) UpdateTrailer();
        if (e.PropertyName == nameof(MainViewModel.SettingsRowIndex) && Vm is { SettingsRowIndex: >= 0 } settings)
            Dispatcher.UIThread.Post(() =>
            {
                if (settings.SettingsRowIndex >= 0 && settings.SettingsRowIndex < SettingsRowList.ItemCount)
                    SettingsRowList.ScrollIntoView(settings.SettingsRowIndex);
            }, DispatcherPriority.Loaded);
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
        // The grid: Up/Down go straight up or down a row from wherever you are in it.
        else if (sender == CoverList && e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None)
        {
            GridUpDown(e.Key == Key.Down ? +1 : -1);
            e.Handled = true;
        }
    }

    /// <summary>How many covers fit in a grid row right now.</summary>
    public int GridColumns
    {
        get
        {
            if (CoverList.ContainerFromIndex(0) is not { } first) return 1;
            var y = first.Bounds.Y;
            var n = 0;
            for (var i = 0; i < CoverList.ItemCount && CoverList.ContainerFromIndex(i) is { } c && Math.Abs(c.Bounds.Y - y) < 1; i++) n++;
            return Math.Max(1, n);
        }
    }

    /// <summary>
    /// One row down (to the last cover when the row below is shorter) or up. Up from the top row goes to the filters.
    /// </summary>
    private void GridUpDown(int direction)
    {
        var count = CoverList.ItemCount;
        if (count == 0) return;
        var cols = GridColumns;
        var index = Math.Max(0, CoverList.SelectedIndex);
        int target;
        if (direction > 0)
        {
            var lastRow = (count - 1) / cols;
            if (index / cols >= lastRow) return;                    // already on the bottom row
            target = Math.Min(index + cols, count - 1);
        }
        else
        {
            if (index < cols) { FocusFilters(); return; }           // top row: up into the filters
            target = index - cols;
        }
        CoverList.SelectedIndex = target;
        CoverList.ScrollIntoView(target);
        if (CoverList.ContainerFromIndex(target) is { } container) container.Focus(NavigationMethod.Directional);
    }

    // ------------------------------------------------------------------ filters (grid), by controller or keyboard
    /// <summary>The filter bar's stops, left to right: each filter pill, More filters, the series (Arcade), Clear.</summary>
    private List<Control> FilterStops()
    {
        var stops = new List<Control>();
        for (var i = 0; i < FilterPills.ItemCount; i++)
            if (FilterPills.ContainerFromIndex(i) is { } pill) stops.Add(pill);
        stops.Add(MoreFilterCombo);
        if (SeriesFilterCombo.IsEffectivelyVisible) stops.Add(SeriesFilterCombo);
        if (ClearFiltersButton.IsEffectivelyVisible) stops.Add(ClearFiltersButton);
        return stops;
    }

    /// <summary>Puts focus on the filter in use (or All Games).</summary>
    public void FocusFilters()
    {
        if (Vm is not { IsGrid: true }) return;
        var stops = FilterStops();
        var target = FilterPills.SelectedIndex >= 0 && FilterPills.ContainerFromIndex(FilterPills.SelectedIndex) is { } pill ? pill
            : MoreFilterCombo.SelectedItem is not null ? MoreFilterCombo
            : stops.FirstOrDefault();
        target?.Focus(NavigationMethod.Directional);
    }

    private void FilterBar_KeyDown(object? sender, KeyEventArgs e)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is null || e.KeyModifiers != KeyModifiers.None) return;
        // An open dropdown: Up/Down choose in it, Enter or Esc close it.
        if (focused is ComboBox { IsDropDownOpen: true } open)
        {
            switch (e.Key)
            {
                case Key.Up: open.SelectedIndex = Math.Max(0, open.SelectedIndex - 1); break;
                case Key.Down: open.SelectedIndex = Math.Min(open.ItemCount - 1, open.SelectedIndex + 1); break;
                case Key.Enter or Key.Escape or Key.Space: open.IsDropDownOpen = false; open.Focus(NavigationMethod.Directional); break;
                default: return;
            }
            e.Handled = true;
            return;
        }
        var stops = FilterStops();
        var stop = focused.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item && stops.Contains(item) ? item : focused;
        var at = stops.IndexOf(stop);
        if (at < 0) return;
        switch (e.Key)
        {
            case Key.Left or Key.Right:
                var next = at + (e.Key == Key.Right ? 1 : -1);
                if (next >= 0 && next < stops.Count) stops[next].Focus(NavigationMethod.Directional);
                break;
            case Key.Enter or Key.Space:
                if (stop is ListBoxItem pill) FilterPills.SelectedIndex = FilterPills.IndexFromContainer(pill);
                else if (stop is ComboBox combo) combo.IsDropDownOpen = true;
                else if (stop is Button button) button.Command?.Execute(null);
                // Choosing a filter can rebuild the bar (Clear disappears): keep focus somewhere sensible.
                Dispatcher.UIThread.Post(() =>
                {
                    if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control { IsEffectivelyVisible: true })
                        FocusFilters();
                }, DispatcherPriority.Loaded);
                break;
            case Key.Down or Key.Escape:
                FocusSelectedCover();
                break;
            default:
                return;
        }
        e.Handled = true;
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
        // Who is closing the window, and from where (to track down S-Dock closing by itself).
        EventLog.Write($"Window closing (reason: {e.CloseReason}, programmatic: {e.IsProgrammatic}, game running: {Vm?.Launcher.IsRunning})" +
                       Environment.NewLine + Environment.StackTrace);
        // A close request from outside (e.g. Alt+F4 that Steam Input sends for Start + Select) right after leaving a
        // game was meant for the game, not S-Dock: ignore it.
        if (!e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing && Vm is { JustPressedExit: true })
        {
            e.Cancel = true;
            EventLog.Write("Ignored: a close request right after leaving a game (it was meant for the game)");
            return;
        }
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

        if (vm.Items.Count == 0)
        {
            // An empty library (e.g. a smoke test of a fresh build): nothing to scroll; the window started fine.
            report.AppendLine("No games in this library: frame timings skipped.");
            return report.ToString();
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
