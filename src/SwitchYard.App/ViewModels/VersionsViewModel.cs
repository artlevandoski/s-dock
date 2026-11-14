using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchYard.Core;

namespace SwitchYard.App.ViewModels;

public sealed partial class VersionRow(EditionFile file) : ObservableObject
{
    public EditionFile File { get; } = file;
    public string FileName => Path.GetFileName(File.Path);
    public string Folder => Path.GetDirectoryName(File.Path) ?? "";
    public bool Exists { get; } = file.Exists;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge))]
    public partial bool IsMain { get; set; } = file.Role == "primary";

    public string Badge => IsMain ? "Main file (Play)" : Exists ? "" : "Missing";

    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (VersionTag(FileName) is { } v) parts.Add(v);
            if (Exists) parts.Add(SizeText(new FileInfo(File.Path).Length));
            parts.Add(Folder);
            return string.Join(" · ", parts);
        }
    }

    private static string? VersionTag(string name) =>
        Regex.Match(name, @"\[v(\d+)\]", RegexOptions.IgnoreCase) is { Success: true } m ? $"v{m.Groups[1].Value}" : null;

    private static string SizeText(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}

/// <summary>Play Version…: every file of a game, with Play, Make Main File and Remove from Library.</summary>
public sealed partial class VersionsViewModel : ObservableObject
{
    private readonly LibraryStore _library;
    private readonly IUiServices _ui;
    private readonly string _editionId;

    public string Title { get; }
    public ObservableCollection<VersionRow> Rows { get; } = [];
    public event Action<string?>? CloseRequested;

    public VersionsViewModel(Edition edition, LibraryStore library, IUiServices ui)
    {
        _library = library;
        _ui = ui;
        _editionId = edition.Id;
        Title = edition.Title;
        Reload();
    }

    public string Heading => $"Versions of “{Title}”";
    public string Explanation => "Play launches the main file. Pick another version to play it once, or make it the main file.";

    private void Reload(long? selectFileId = null)
    {
        var edition = _library.GetEdition(_editionId);
        Rows.Clear();
        if (edition is null) return;
        foreach (var f in edition.Files.OrderByDescending(f => f.Role == "primary").ThenBy(f => f.Id))
            Rows.Add(new VersionRow(f));
        Selected = Rows.FirstOrDefault(r => r.File.Id == selectFileId) ?? Rows.FirstOrDefault();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(MakeMainCommand), nameof(RemoveCommand))]
    public partial VersionRow? Selected { get; set; }

    private bool CanPlay() => Selected is { Exists: true };
    private bool CanMakeMain() => Selected is { IsMain: false };
    private bool CanRemove() => Selected is not null && Rows.Count > 1;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => CloseRequested?.Invoke(Selected?.File.Path);

    [RelayCommand(CanExecute = nameof(CanMakeMain))]
    private void MakeMain()
    {
        if (Selected is not { } row) return;
        _library.SetPrimaryFile(row.File.Id);
        Reload(row.File.Id);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        if (Selected is not { } row) return;
        if (!await _ui.ConfirmAsync("Remove version",
                $"Remove {row.FileName} from this game's versions?\n\nThe file itself is not deleted or moved."))
            return;
        _library.RemoveFile(row.File.Id);
        Reload();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(null);
}
