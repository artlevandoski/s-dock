using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDock.App.Input;
using SDock.Core;

namespace SDock.App.ViewModels;

public enum SettingKind
{
    /// <summary>On or off (A, Enter or a click flips it).</summary>
    Toggle,
    /// <summary>One of a few values, shown "‹ value ›" (Left/Right or the arrows change it; A moves to the next).</summary>
    Choice,
    /// <summary>Does something or opens a dialog (A, Enter or a click); Settings stays open afterwards.</summary>
    Action,
    /// <summary>Just information.</summary>
    Info,
}

/// <summary>One line on the Settings page.</summary>
public sealed partial class SettingRow : ObservableObject
{
    public required string Label { get; init; }
    public string Description { get; init; } = "";
    public SettingKind Kind { get; init; }
    public Func<string>? Value { get; init; }
    public Func<bool>? IsOn { get; init; }
    public Func<bool>? IsEnabled { get; init; }
    /// <summary>Toggle: flips it. Action: runs it.</summary>
    public Func<Task>? Run { get; init; }
    /// <summary>Choice: to the next (+1) or previous (-1) value.</summary>
    public Action<int>? Step { get; init; }

    public string ValueText => Value?.Invoke() ?? "";
    public bool HasValue => ValueText.Length > 0 && Kind is SettingKind.Action or SettingKind.Info;
    public bool On => IsOn?.Invoke() ?? false;
    public bool Enabled => IsEnabled?.Invoke() ?? true;
    public double Opacity => Enabled ? 1 : 0.45;
    public bool IsToggle => Kind == SettingKind.Toggle;
    public bool IsChoice => Kind == SettingKind.Choice;
    public bool IsAction => Kind == SettingKind.Action;
    public bool HasDescription => Description.Length > 0;
    /// <summary>Information lines can't be landed on with the D-pad.</summary>
    public bool Selectable => Kind != SettingKind.Info;

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>A section in the Settings page's left column.</summary>
public sealed record SettingSection(string Title, string IconKey, string Description, Func<IReadOnlyList<SettingRow>> Build);

/// <summary>
/// The Settings page: a full-screen page (the same in the grid and the carousel) with sections on the left and their
/// settings on the right. Controller, keyboard and mouse all drive it the same way:
///   Up/Down move; A, Enter or Right go into a section; on a setting, A/Enter (or a click) flips a switch, runs an
///   action or moves a choice on, and Left/Right change a choice; B or Esc go back one step (setting → sections →
///   closed). Dialogs opened from here return to the same setting.
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] public partial bool IsSettingsOpen { get; set; }
    /// <summary>True when the D-pad is on the settings (right) rather than the sections (left).</summary>
    [ObservableProperty] public partial bool SettingsInRows { get; set; }
    [ObservableProperty] public partial int SettingsSectionIndex { get; set; } = -1;
    [ObservableProperty] public partial int SettingsRowIndex { get; set; } = -1;

    public ObservableCollection<SettingSection> SettingsSections { get; } = [];
    public ObservableCollection<SettingRow> SettingsRows { get; } = [];
    public SettingSection? SettingsSection => SettingsSectionIndex >= 0 && SettingsSectionIndex < SettingsSections.Count
        ? SettingsSections[SettingsSectionIndex] : null;
    public SettingRow? SettingsRow => SettingsRowIndex >= 0 && SettingsRowIndex < SettingsRows.Count ? SettingsRows[SettingsRowIndex] : null;
    public string SettingsBackLabel => SettingsInRows ? "Back" : "Close";

    /// <summary>Raised when the page opens or closes (the view moves focus).</summary>
    public event Action? SettingsChanged;

    [RelayCommand]
    public void OpenSettings()
    {
        CloseMenu();
        SettingsSections.Clear();
        foreach (var s in BuildSettingsSections()) SettingsSections.Add(s);
        SettingsInRows = false;
        SettingsSectionIndex = 0;
        IsSettingsOpen = true;
        SettingsChanged?.Invoke();
    }

    [RelayCommand]
    public void CloseSettings()
    {
        if (!IsSettingsOpen) return;
        IsSettingsOpen = false;
        SettingsInRows = false;
        SettingsChanged?.Invoke();
    }

    partial void OnSettingsSectionIndexChanged(int value)
    {
        RebuildSettingsRows(keepIndex: false);
        OnPropertyChanged(nameof(SettingsSection));
    }

    partial void OnSettingsRowIndexChanged(int value) => OnPropertyChanged(nameof(SettingsRow));
    partial void OnSettingsInRowsChanged(bool value) => OnPropertyChanged(nameof(SettingsBackLabel));

    private void RebuildSettingsRows(bool keepIndex)
    {
        var keep = keepIndex ? SettingsRowIndex : -1;
        SettingsRows.Clear();
        if (SettingsSection is { } section)
            foreach (var r in section.Build()) SettingsRows.Add(r);
        SettingsRowIndex = keep >= 0 && keep < SettingsRows.Count && SettingsRows[keep].Selectable
            ? keep
            : SettingsInRows ? FirstSelectableRow() : -1;
    }

    private int FirstSelectableRow()
    {
        for (var i = 0; i < SettingsRows.Count; i++)
            if (SettingsRows[i].Selectable) return i;
        return -1;
    }

    /// <summary>Values can depend on each other (and on what dialogs changed): show them afresh.</summary>
    public void RefreshSettings()
    {
        if (!IsSettingsOpen) return;
        foreach (var r in SettingsRows) r.Refresh();
    }

    /// <summary>A controller (or keyboard) press while Settings is open.</summary>
    public void SettingsInput(PadAction action)
    {
        if (!IsSettingsOpen) return;
        switch (action)
        {
            case PadAction.Up or PadAction.Down:
                var step = action == PadAction.Down ? 1 : -1;
                if (!SettingsInRows)
                    SettingsSectionIndex = Math.Clamp(SettingsSectionIndex + step, 0, SettingsSections.Count - 1);
                else
                    for (var i = SettingsRowIndex + step; i >= 0 && i < SettingsRows.Count; i += step)
                        if (SettingsRows[i].Selectable) { SettingsRowIndex = i; break; }
                break;
            case PadAction.Right:
                if (!SettingsInRows) EnterSettingsRows();
                else if (SettingsRow is { IsChoice: true } choice) StepSetting(choice, +1);
                break;
            case PadAction.Left:
                if (SettingsRow is { IsChoice: true } back && SettingsInRows) StepSetting(back, -1);
                else if (SettingsInRows) SettingsInRows = false;
                break;
            case PadAction.Accept:
                if (!SettingsInRows) EnterSettingsRows();
                else if (SettingsRow is { } row) _ = ActivateSettingAsync(row);
                break;
            case PadAction.Cancel or PadAction.Menu:
                if (SettingsInRows && action == PadAction.Cancel) SettingsInRows = false;
                else CloseSettings();
                break;
        }
    }

    private void EnterSettingsRows()
    {
        if (FirstSelectableRow() < 0) return;
        SettingsInRows = true;
        if (SettingsRow is not { Selectable: true }) SettingsRowIndex = FirstSelectableRow();
    }

    /// <summary>A, Enter or a click on a setting.</summary>
    public async Task ActivateSettingAsync(SettingRow row)
    {
        if (!row.Enabled) return;
        var index = SettingsRows.IndexOf(row);
        if (index >= 0) { SettingsInRows = true; SettingsRowIndex = index; }
        switch (row.Kind)
        {
            case SettingKind.Choice:
                StepSetting(row, +1);
                break;
            case SettingKind.Toggle or SettingKind.Action when row.Run is { } run:
                await run();
                if (IsSettingsOpen) RebuildSettingsRows(keepIndex: true); // a dialog may have changed what's shown
                break;
        }
    }

    /// <summary>Left/Right on a choice, or a click on its arrows.</summary>
    public void StepSetting(SettingRow row, int direction)
    {
        if (!row.Enabled || row.Step is not { } step) return;
        var index = SettingsRows.IndexOf(row);
        if (index >= 0) SettingsRowIndex = index;
        step(direction);
        RefreshSettings();
    }

    [RelayCommand] private void SettingPrevious(SettingRow row) => StepSetting(row, -1);
    [RelayCommand] private void SettingNext(SettingRow row) => StepSetting(row, +1);

    // ----------------------------------------------------------------- what's on the page
    private static SettingRow Toggle(string label, string description, Func<bool> isOn, Action flip, Func<bool>? enabled = null) =>
        new() { Label = label, Description = description, Kind = SettingKind.Toggle, IsOn = isOn, IsEnabled = enabled,
                Run = () => { flip(); return Task.CompletedTask; } };

    private static SettingRow Choice<T>(string label, string description, IReadOnlyList<(T Value, string Label)> options,
        Func<T> get, Action<T> set)
    {
        string Current() => options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, get())).Label ?? options[0].Label;
        return new SettingRow
        {
            Label = label, Description = description, Kind = SettingKind.Choice, Value = Current,
            Step = d =>
            {
                var i = options.Select((o, n) => (o, n)).FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.o.Value, get())).n;
                set(options[((i + d) % options.Count + options.Count) % options.Count].Value);
            },
        };
    }

    private static SettingRow Action(string label, string description, System.Windows.Input.ICommand command,
        Func<string>? value = null, Func<bool>? enabled = null) =>
        new()
        {
            Label = label, Description = description, Kind = SettingKind.Action, Value = value, IsEnabled = enabled,
            Run = async () =>
            {
                if (command is IAsyncRelayCommand async) await async.ExecuteAsync(null);
                else command.Execute(null);
            },
        };

    private static SettingRow Info(string label, Func<string> value, string description = "") =>
        new() { Label = label, Description = description, Kind = SettingKind.Info, Value = value };

    private IReadOnlyList<SettingSection> BuildSettingsSections() =>
    [
        new("Library", "IconLayers", "Your games, how they're shown and filtered, and adding more.", () =>
        [
            Choice("Launch view", "The view S-Dock opens in. Y switches between them any time.",
                [(false, "Grid"), (true, "Carousel")], () => LaunchInCarousel, v => LaunchInCarousel = v),
            Choice("Filter", "Which games are shown in both views.",
                FilterOption.Generation.Select(f => (f, f.Label)).ToList(), () => GenerationFilter, v => GenerationFilter = v),
            Toggle("Include ACA NEOGEO", "Show ACA NEOGEO titles in Arcade Archives instead of Main Library.",
                () => IncludeAcaNeogeo, () => IncludeAcaNeogeo = !IncludeAcaNeogeo),
            Toggle("Include SEGA AGES", "Show SEGA AGES titles in Arcade Archives instead of Main Library.",
                () => IncludeSegaAges, () => IncludeSegaAges = !IncludeSegaAges),
            Action("Add Folder…", "Add a folder of game files (.nsp, .xci, .nsz, .xcz).", AddFolderCommand),
            Action("Add Game (.exe)…", "Add a PC version (a recompilation or port) that runs without an emulator.", AddProgramCommand),
            Choice("Start on", "The game selected when S-Dock opens.",
                [(true, "Last game played"), (false, "Last game selected")], () => StartOnLastPlayedGame, v => StartOnLastPlayedGame = v),
            Action("Clear Play History…", "Forget when games were played and for how long. Recently Played empties.",
                ClearPlayHistoryCommand, () => PlayedCountText),
            Action("Move Game Folder…", "Games are somewhere else now (another PC or drive)? Point their folder at the new place; every game keeps its settings.",
                MoveGameFolderCommand, () => MissingGamesText),
        ]),
        new("Emulators", "IconGamepad", "Emulator profiles and which one each kind of game uses.", () =>
        [
            Action("Emulator Profiles…", "Add, edit or remove emulators, and try a launch.", EmulatorSettingsCommand,
                () => _library.ListProfiles() is { Count: > 0 } p ? (p.Count == 1 ? "1 profile" : $"{p.Count} profiles") : "None yet"),
            DefaultEmulatorChoice(Generations.Switch1),
            DefaultEmulatorChoice(Generations.Switch2),
            Action("Open Emulators Folder", "Put emulator builds here and they're found automatically.", OpenEmulatorsFolderCommand),
        ]),
        new("Playing", "IconPlay", "Starting and leaving games.", () =>
        [
            Toggle("Start games in full screen", "Adds Eden's full-screen switch when a game starts.",
                () => StartGamesFullScreen, () => StartGamesFullScreen = !StartGamesFullScreen),
            Choice("Leave a game with Esc or Start + Select", "Twice is safer: the first press shows a note over the game.",
                [(false, "Press once"), (true, "Press twice")], () => ExitWithTwoPresses, v => ExitWithTwoPresses = v),
            Info("Controllers", () => ControllersText(),
                "Xbox, PlayStation, Switch Pro and most others. A (or ✕) chooses, B (or ○) goes back."),
        ]),
        new("Appearance", "IconImage", "How S-Dock looks.", () =>
        [
            Choice("Background", "What's shown behind the covers.",
                [(BackgroundArt, "Game artwork"), (BackgroundColor, "Color from game"), (BackgroundPlain, "Plain")],
                () => BackgroundStyle, v => BackgroundStyle = v),
        ]),
        new("Game Info", "IconTune", "Names, descriptions, artwork and logos from online sources.", () =>
        {
            var rows = new List<SettingRow>();
            if (IsMatchingAll)
            {
                rows.Add(Info("Working", () => StatusText));
                rows.Add(Action("Cancel", "Stop the running update. Everything done so far is kept.", CancelMatchingCommand));
            }
            rows.Add(Action("Update from TitleDB", "The eShop's names, descriptions, icons and banners, matched by title ID.",
                UpdateFromTitleDbCommand, () => TitleDb.Downloaded is { } d ? $"Updated {d:d MMM yyyy}" : "Not downloaded yet",
                () => !IsMatchingAll));
            rows.Add(Toggle("Use nx-versions", "Latest known update versions and release dates, refreshed with TitleDB.",
                () => UseNxVersions, () => UseNxVersions = !UseNxVersions));
            rows.Add(Toggle("Show EmuReady ratings",
                "How each game runs on PC emulators, from the community reports at emuready.com. Looked up as you browse.",
                () => ShowEmuReady, () => ShowEmuReady = !ShowEmuReady));
            rows.Add(Action("Match unmatched games on IGDB", "For games TitleDB doesn't list. Needs IGDB keys.",
                MatchUnmatchedCommand, enabled: () => !IsMatchingAll));
            rows.Add(Action("Get missing logos", "Logos from IGDB, then SteamGridDB.", GetMissingLogosCommand, enabled: () => !IsMatchingAll));
            rows.Add(Action("IGDB and SteamGridDB keys…", "Your own free keys, kept only on this PC.", IgdbSettingsCommand,
                () => _library.HasIgdbCredentials ? (_library.HasSteamGridDbKey ? "IGDB and SteamGridDB set" : "IGDB set") : "Not set"));
            return rows;
        }),
        new("About", "IconMenu", "Version, updates, files and quitting.", () =>
        [
            Info("Version", () => $"S-Dock {AppVersion}"),
            Action("Check for updates", "New versions come from S-Dock's GitHub releases. Your library and emulators are kept.",
                CheckForUpdatesCommand, () => UpdateStatusLine),
            Toggle("Check for updates when S-Dock starts", "Asks before updating; never during a game.",
                () => CheckForUpdatesOnStart, () => CheckForUpdatesOnStart = !CheckForUpdatesOnStart),
            Action("Open Data Folder", "Your library, artwork and settings, all inside the S-Dock folder.", OpenDataFolderCommand,
                () => AppPaths.DataDir),
            Action("Quit S-Dock", "Close S-Dock.", QuitCommand),
        ]),
    ];

    private SettingRow DefaultEmulatorChoice(string generation)
    {
        var options = new List<(string? Value, string Label)> { (null, "Ask each time") };
        if (generation == Generations.Switch2) options.Add((LaunchRouter.NoEmulatorId, "No emulator (run the game's .exe)"));
        options.AddRange(_library.ListProfiles().Where(p => p.Supports(generation)).Select(p => ((string?)p.Id, p.Name)));
        return Choice($"{Generations.Label(generation)} default", $"The emulator {Generations.Label(generation)} games use unless a game has its own choice.",
            options, () => _library.GetGenerationDefault(generation), v =>
            {
                _library.SetGenerationDefault(generation, v);
                RebuildEmulatorChoices();
            });
    }

    /// <summary>The controllers S-Dock sees right now (tests replace it).</summary>
    public Func<IReadOnlyList<string>> ConnectedControllers { get; set; } = () => GamepadService.ControllerNames;

    private string ControllersText() => ConnectedControllers() is { Count: > 0 } names ? string.Join(", ", names) : "None connected";

    [RelayCommand] private void Quit() => QuitRequested?.Invoke();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!IsSettingsOpen) return;
        // A running update adds a progress line and a Cancel button to Game Info; its progress text updates live.
        if (e.PropertyName == nameof(IsMatchingAll)) RebuildSettingsRows(keepIndex: true);
        else if (e.PropertyName == nameof(StatusText)) RefreshSettings();
    }
}
