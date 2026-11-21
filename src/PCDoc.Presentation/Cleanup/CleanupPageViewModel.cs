using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Cleanup;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Cleanup;

/// <summary>Une catégorie à cocher.</summary>
public sealed partial class CategoryRow(string id, string title, string explanation, string size, long bytes, Finding finding, bool canSelect, string riskLabel)
    : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Id { get; } = id;

    public string Title { get; } = title;

    public string Explanation { get; } = explanation;

    public string Size { get; } = size;

    public long Bytes { get; } = bytes;

    public Finding Finding { get; } = finding;

    /// <summary>Faux en mode novice pour 🟠 (vidages, corbeille).</summary>
    public bool CanSelect { get; } = canSelect;

    public string RiskLabel { get; } = riskLabel;
}

public sealed record BigFileRow(string Path, string Size);

/// <summary>Page Nettoyage : catégories cochables, corbeille, anciennes mises à jour, gros fichiers.</summary>
public sealed partial class CleanupPageViewModel : ObservableObject
{
    private readonly ICleanupSource _source;
    private readonly ILocalizer _localizer;
    private readonly IClipboardService? _clipboard;
    private readonly Func<Core.Settings.AppMode> _mode;
    private CleanupReport? _report;
    private IReadOnlyList<Core.Abstractions.FileEntry> _bigFiles = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _totalText = string.Empty;

    public CleanupPageViewModel(ICleanupSource source, ILocalizer localizer, FixConfirmationViewModel? fixes, IClipboardService? clipboard,
        Func<Core.Settings.AppMode>? mode = null)
    {
        _source = source;
        _localizer = localizer;
        _clipboard = clipboard;
        _mode = mode ?? (() => Core.Settings.AppMode.Novice);
        Fixes = fixes;
        if (fixes is not null)
        {
            fixes.Completed += (_, _) => _ = AnalyzeAsync();
        }

        localizer.PropertyChanged += (_, _) => Build();
    }

    public FixConfirmationViewModel? Fixes { get; }

    public ObservableCollection<CategoryRow> Categories { get; } = [];

    public ObservableCollection<BigFileRow> BigFiles { get; } = [];

    public bool HasLoaded => _report is not null;

    public Task EnsureLoadedAsync() => HasLoaded ? Task.CompletedTask : AnalyzeAsync();

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        IsLoading = true;
        try
        {
            (_report, _bigFiles) = await Task.Run(() => (_source.Analyze(), _source.BigFiles())).ConfigureAwait(true);
            Build();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CleanSelected() => Fixes?.Request(Categories.Where(c => c.IsSelected && c.CanSelect).Select(c => c.Finding));

    [RelayCommand]
    private void CopyPath(BigFileRow? row)
    {
        if (row is not null)
        {
            _clipboard?.SetText(row.Path);
        }
    }

    private string Size(long bytes) => bytes >= 1L << 30
        ? _localizer.Format(new LocalizedText("Size.GB", bytes / (double)(1L << 30)))
        : _localizer.Format(new LocalizedText("Size.MB", bytes / (double)(1L << 20)));

    private void Build()
    {
        if (_report is not { } r)
        {
            return;
        }

        var previous = Categories.ToDictionary(c => c.Id, c => c.IsSelected);
        Categories.Clear();
        var items = r.Categories.Select(c => (c.Id, c.Risk, c.Bytes, c.Files, Known: true))
            .Append((CleanupEngine.RecycleBinId, RiskLevel.Conditional, r.RecycleBinBytes, (int)r.RecycleBinItems, true))
            .Append((CleanupEngine.ComponentStoreId, RiskLevel.Safe, -1L, 0, true));
        foreach (var (id, risk, bytes, files, _) in items)
        {
            var finding = CleanupFixer.FindingFor(id, risk, Math.Max(0, bytes), files);
            var canSelect = FixPolicy.IsOffered(finding, _mode());
            var row = new CategoryRow(id, _localizer[$"Cleanup.{id}.Title"], _localizer[$"Cleanup.{id}.Explanation"],
                bytes < 0 ? "—" : Size(bytes), Math.Max(0, bytes), finding, canSelect, _localizer[$"Risk.{risk}"]);
            row.IsSelected = previous.TryGetValue(id, out var was) ? was && canSelect : canSelect && risk == RiskLevel.Safe && bytes > 0;
            row.PropertyChanged += (_, _) => UpdateTotal();
            Categories.Add(row);
        }

        BigFiles.Clear();
        foreach (var f in _bigFiles)
        {
            BigFiles.Add(new BigFileRow(f.Path, Size(f.Size)));
        }

        UpdateTotal();
        OnPropertyChanged(nameof(HasLoaded));
    }

    private void UpdateTotal() =>
        TotalText = _localizer.Format(new LocalizedText("Cleanup.Total", Size(Categories.Where(c => c.IsSelected).Sum(c => c.Bytes))));
}
