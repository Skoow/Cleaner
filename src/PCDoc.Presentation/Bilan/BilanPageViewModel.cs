using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core;
using PCDoc.Core.Bilan;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;

namespace PCDoc.Presentation.Bilan;

/// <summary>État de la page Bilan.</summary>
public enum BilanState
{
    Idle,
    Running,
    Done,
}

/// <summary>Un filtre par module (Id null = tous).</summary>
public sealed record ModuleFilterOption(string? Id, string Label);

/// <summary>Étape affichée pendant l'analyse (un module).</summary>
public sealed partial class ProgressStep(string id, string label) : ObservableObject
{
    [ObservableProperty]
    private bool _done;

    public string Id { get; } = id;

    public string Label { get; } = label;
}

/// <summary>Page d'accueil : « Analyser mon PC », note, problèmes classés par gravité.</summary>
public sealed partial class BilanPageViewModel : ObservableObject
{
    private readonly BilanOrchestrator _orchestrator;
    private readonly ILocalizer _localizer;
    private readonly IClipboardService? _clipboard;
    private BilanResult? _result;
    private TimeSpan _duration;

    [ObservableProperty]
    private BilanState _state;

    [ObservableProperty]
    private string? _moduleFilter;

    [ObservableProperty]
    private IReadOnlyList<ModuleFilterOption> _moduleFilters = [];

    [ObservableProperty]
    private IReadOnlyList<ProgressStep> _steps = [];

    public BilanPageViewModel(BilanOrchestrator orchestrator, ILocalizer localizer, IClipboardService? clipboard = null)
    {
        _orchestrator = orchestrator;
        _localizer = localizer;
        _clipboard = clipboard;
        localizer.PropertyChanged += (_, _) => RefreshTexts();
    }

    public ObservableCollection<FindingRowViewModel> Findings { get; } = [];

    public System.Windows.Input.ICommand CancelCommand => AnalyzeCancelCommand;

    public int Score => _result?.Score ?? 0;

    public string ScoreLabel => _localizer[HealthScore.LabelKey(Score)];

    public string ScoreAccessibleName => _localizer.Format(new LocalizedText("Bilan.ScoreAlt", Score, ScoreLabel));

    public int CriticalCount => Count(Severity.Critical);

    public int WarningCount => Count(Severity.Warning);

    public int InfoCount => Count(Severity.Info);

    public string CriticalText => _localizer.Format(new LocalizedText("Bilan.Count.Critical", CriticalCount));

    public string WarningText => _localizer.Format(new LocalizedText("Bilan.Count.Warning", WarningCount));

    public string InfoText => _localizer.Format(new LocalizedText("Bilan.Count.Info", InfoCount));

    public string CompletedText => _result is null
        ? string.Empty
        : _localizer.Format(new LocalizedText("Bilan.Completed", _result.CompletedAt, _duration.TotalSeconds));

    public string? ErrorText => _result is { Errors.Count: > 0 } r
        ? _localizer.Format(new LocalizedText("Bilan.ModuleError",
            string.Join(", ", r.Errors.Select(e => _localizer[$"Module.{e.Module}"]))))
        : null;

    public bool AnyFixAvailable => _result?.Findings.Any(f => f.FixAvailable) == true;

    public bool HasNoFindings => State == BilanState.Done && _result is { Findings.Count: 0 };

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        State = BilanState.Running;
        Steps = [.. ModuleIds().Select(id => new ProgressStep(id, _localizer[$"Module.{id}"]))];
        var progress = new Progress<string>(id =>
        {
            foreach (var step in Steps.Where(s => s.Id == id))
            {
                step.Done = true;
            }
        });

        var watch = Stopwatch.StartNew();
        try
        {
            _result = await _orchestrator.RunAsync(progress, cancellationToken).ConfigureAwait(true);
            _duration = watch.Elapsed;
            State = BilanState.Done;
        }
        catch (OperationCanceledException)
        {
            _result = null;
            State = BilanState.Idle;
        }

        BuildFilters();
        ApplyFilter();
        RefreshTexts();
    }

    [RelayCommand]
    private void CopyFinding(FindingRowViewModel? row)
    {
        if (row is not null)
        {
            _clipboard?.SetText($"{row.SeverityLabel} — {row.Title}\n{row.Explanation}\n\n{row.TechnicalDetails}");
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (_result is null)
        {
            return;
        }

        var text = new StringBuilder()
            .AppendLine(_localizer.Format(new LocalizedText("Bilan.ReportHeader", AppInfo.ProductName, AppInfo.Version, _result.CompletedAt)))
            .AppendLine(FormattableString.Invariant($"{Score}/100 — {ScoreLabel}"))
            .AppendLine(string.Join(" · ", CriticalText, WarningText, InfoText));
        if (ErrorText is { } error)
        {
            text.AppendLine(error);
        }

        foreach (var f in _result.Findings)
        {
            var row = new FindingRowViewModel(f, _localizer);
            text.AppendLine().Append('[').Append(row.SeverityLabel).Append("] ").AppendLine(row.Title)
                .AppendLine(row.Explanation)
                .AppendLine(row.TechnicalDetails);
        }

        _clipboard?.SetText(text.ToString());
    }

    partial void OnModuleFilterChanged(string? value) => ApplyFilter();

    partial void OnStateChanged(BilanState value) => OnPropertyChanged(nameof(HasNoFindings));

    private IEnumerable<string> ModuleIds() => _orchestrator.ModuleIds;

    private int Count(Severity severity) => _result?.Findings.Count(f => f.Severity == severity) ?? 0;

    private void BuildFilters() =>
        ModuleFilters = _result is null
            ? []
            : [new ModuleFilterOption(null, _localizer["Bilan.AllModules"]),
               .. _result.Findings.Select(f => f.Module).Distinct().Order(StringComparer.Ordinal)
                   .Select(m => new ModuleFilterOption(m, _localizer[$"Module.{m}"]))];

    private void ApplyFilter()
    {
        Findings.Clear();
        if (_result is null)
        {
            return;
        }

        foreach (var f in _result.Findings.Where(f => ModuleFilter is null || f.Module == ModuleFilter))
        {
            Findings.Add(new FindingRowViewModel(f, _localizer) { IsExpanded = Findings.Count == 0 });
        }
    }

    private void RefreshTexts()
    {
        foreach (var row in Findings)
        {
            row.RefreshTexts();
        }

        if (_result is not null)
        {
            BuildFilters();
        }

        OnPropertyChanged(string.Empty);
    }
}
