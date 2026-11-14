using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchYard.Core;

namespace SwitchYard.App.ViewModels;

public sealed partial class ImportRow : ObservableObject
{
    private readonly ImportPreviewViewModel _owner;
    public ScanEntry Entry { get; }

    public ImportRow(ImportPreviewViewModel owner, ScanEntry entry)
    {
        _owner = owner;
        Entry = entry;
    }

    public IReadOnlyList<GenerationOption> Options => GenerationOption.Library;
    public bool IsEditable => !Entry.InLibrary;
    public string KindLabel => ContentKinds.Label(Entry.Kind);
    public string TitleId => Entry.TitleId ?? "";
    public string Path => Entry.Path;
    public string Notes => Entry.Notes;
    public bool IsFlagged => Entry.Flags.Any(f => f != ScanFlags.InLibrary);

    public bool Include
    {
        get => Entry.Include;
        set
        {
            if (Entry.InLibrary || Entry.Include == value) return;
            Entry.Include = value;
            OnPropertyChanged();
            _owner.UpdateSummary();
        }
    }

    public string Title
    {
        get => Entry.Title;
        set
        {
            var t = value.Trim();
            if (t.Length == 0 || t == Entry.Title) return;
            Entry.Title = t;
            OnPropertyChanged();
        }
    }

    public GenerationOption GenerationOption
    {
        get => GenerationOption.ForLibrary(Entry.Generation);
        set
        {
            if (value is null || value.Value == Entry.Generation) return;
            Entry.Generation = value.Value;
            Entry.GenerationSource = GenerationSources.Manual;
            Entry.Flags.Remove(ScanFlags.GenerationConflict);
            Refresh();
            _owner.UpdateSummary();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(GenerationOption));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(IsFlagged));
    }
}

public sealed partial class ImportPreviewViewModel : ObservableObject
{
    public ScanResult Result { get; }
    public ObservableCollection<ImportRow> Rows { get; } = [];
    public IReadOnlyList<GenerationOption> FolderOptions => GenerationOption.Library;

    public event Action<bool>? CloseRequested;

    public ImportPreviewViewModel(ScanResult result, string folderGeneration)
    {
        Result = result;
        FolderGeneration = GenerationOption.ForLibrary(folderGeneration);
        foreach (var e in result.Entries)
            Rows.Add(new ImportRow(this, e));
        UpdateSummary();
    }

    public string Header =>
        $"{Result.Entries.Count} game file(s) found in {Result.Folder}. Nothing is imported until you click Import. " +
        "Your files are never moved or renamed.";

    [ObservableProperty]
    public partial GenerationOption FolderGeneration { get; set; }

    partial void OnFolderGenerationChanged(GenerationOption value)
    {
        LibraryScanner.ApplyFolderGeneration(Result.Entries.Where(e => !e.InLibrary), value.Value);
        foreach (var r in Rows) r.Refresh();
        UpdateSummary();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial string Summary { get; set; } = "";

    public bool CanImport => Result.Entries.Any(e => e.InLibrary || e.Include);

    public bool HasWarnings => Result.MissingKnown.Count > 0 || Result.Errors.Count > 0;

    public string Warnings
    {
        get
        {
            var lines = new List<string>();
            if (Result.MissingKnown.Count > 0)
            {
                lines.Add($"{Result.MissingKnown.Count} previously imported file(s) under this folder are missing " +
                          "and will be shown as FILE MISSING in the library:");
                lines.AddRange(Result.MissingKnown.Take(8).Select(p => "    " + p));
                if (Result.MissingKnown.Count > 8) lines.Add("    …");
            }
            if (Result.Errors.Count > 0)
            {
                lines.Add($"{Result.Errors.Count} location(s) could not be read:");
                lines.AddRange(Result.Errors.Take(5).Select(e => "    " + e));
            }
            return string.Join("\n", lines);
        }
    }

    public void UpdateSummary()
    {
        var entries = Result.Entries;
        var toImport = entries.Count(e => e.Include && !e.InLibrary);
        var present = entries.Count(e => e.InLibrary);
        var flagged = entries.Count(e => !e.InLibrary && e.Flags.Any(f => f != ScanFlags.InLibrary));
        var unclassified = entries.Count(e => e.Include && !e.InLibrary && e.Generation == Generations.Unclassified);
        var text = $"{toImport} to import · {present} already in library · {flagged} flagged for review";
        if (unclassified > 0) text += $" · {unclassified} will be imported as Unclassified";
        Summary = text;
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
