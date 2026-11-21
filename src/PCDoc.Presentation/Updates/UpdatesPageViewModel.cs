using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Updates;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Updates;

/// <summary>Une ligne cochable (mise à jour Windows, logiciel à mettre à jour ou logiciel à installer).</summary>
public sealed partial class UpdateRow(string title, string detail, string riskLabel, Finding finding, bool canSelect) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string RiskLabel { get; } = riskLabel;

    public Finding Finding { get; } = finding;

    public bool CanSelect { get; } = canSelect;
}

/// <summary>Page Mises à jour : Windows et pilotes, logiciels (winget), installation groupée.</summary>
public sealed partial class UpdatesPageViewModel : ObservableObject
{
    private readonly IUpdatesSource _source;
    private readonly ILocalizer _localizer;
    private readonly Func<Core.Settings.AppMode> _mode;
    private readonly Func<bool> _rebootCheck;
    private UpdatesReport? _report;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _windowsStatus;

    [ObservableProperty]
    private string? _softwareStatus;

    [ObservableProperty]
    private bool _rebootRequired;

    public UpdatesPageViewModel(IUpdatesSource source, ILocalizer localizer, FixConfirmationViewModel? fixes,
        Func<Core.Settings.AppMode>? mode = null, Func<bool>? rebootRequired = null)
    {
        _source = source;
        _localizer = localizer;
        _mode = mode ?? (() => Core.Settings.AppMode.Novice);
        _rebootCheck = rebootRequired ?? (() => false);
        Fixes = fixes;
        if (fixes is not null)
        {
            fixes.Completed += (_, _) => _ = AnalyzeAsync();
        }

        localizer.PropertyChanged += (_, _) => Build();
    }

    public FixConfirmationViewModel? Fixes { get; }

    public ObservableCollection<UpdateRow> Windows { get; } = [];

    public ObservableCollection<UpdateRow> Software { get; } = [];

    public ObservableCollection<UpdateRow> Catalog { get; } = [];

    public bool HasLoaded => _report is not null;

    public Task EnsureLoadedAsync() => HasLoaded ? Task.CompletedTask : AnalyzeAsync();

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        IsLoading = true;
        try
        {
            _report = await _source.AnalyzeAsync(includeWindows: true, CancellationToken.None).ConfigureAwait(true);
            Build();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void InstallWindows() => Request(Windows);

    [RelayCommand]
    private void UpgradeSoftware() => Request(Software);

    [RelayCommand]
    private void InstallCatalog() => Request(Catalog);

    private void Request(IEnumerable<UpdateRow> rows) => Fixes?.Request(rows.Where(r => r.IsSelected && r.CanSelect).Select(r => r.Finding));

    private UpdateRow Row(string title, string detail, Finding finding, bool selectByDefault, HashSet<string> previous, bool firstBuild)
    {
        var canSelect = FixPolicy.IsOffered(finding, _mode());
        var row = new UpdateRow(title, detail, _localizer[$"Risk.{finding.Risk}"], finding, canSelect);
        row.IsSelected = canSelect && (firstBuild ? selectByDefault : previous.Contains(finding.Id));
        return row;
    }

    private void Build()
    {
        if (_report is not { } report)
        {
            return;
        }

        var first = Windows.Count == 0 && Software.Count == 0 && Catalog.Count == 0;
        var previous = Windows.Concat(Software).Concat(Catalog).Where(r => r.IsSelected).Select(r => r.Finding.Id).ToHashSet(StringComparer.Ordinal);
        Windows.Clear();
        foreach (var w in report.Windows)
        {
            var finding = UpdateFixer.ForWindows(w);
            var detail = string.Join(" · ", new[]
            {
                w.IsDriver ? _localizer["Updates.Driver"] : null,
                w.IsOptional ? _localizer["Updates.Optional"] : null,
                w.KbArticle is { } kb ? "KB" + kb : null,
                w.SizeBytes > 0 ? _localizer.Format(new LocalizedText("Size.MB", w.SizeBytes / (double)(1L << 20))) : null,
            }.Where(t => t is not null));
            Windows.Add(Row(w.Title, detail, finding, finding.Risk == RiskLevel.Safe, previous, first));
        }

        Software.Clear();
        foreach (var s in report.Software)
        {
            Software.Add(Row(s.Name, $"{s.Version} → {s.Available}", UpdateFixer.ForUpgrade(s), selectByDefault: true, previous, first));
        }

        Catalog.Clear();
        foreach (var p in _source.Catalog.OrderBy(p => p.Category, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Catalog.Add(Row(p.Name, _localizer[$"Updates.Category.{p.Category}"], UpdateFixer.ForInstall(p), selectByDefault: false, previous, first));
        }

        WindowsStatus = report.WindowsUpdateFailed ? _localizer["Updates.WindowsFailed"] : report.Windows.Count == 0 ? _localizer["Updates.None"] : null;
        SoftwareStatus = report.WingetMissing ? _localizer["Updates.WingetMissing"] : report.Software.Count == 0 ? _localizer["Updates.None"] : null;
        RebootRequired = _rebootCheck();
        OnPropertyChanged(nameof(HasLoaded));
    }
}
