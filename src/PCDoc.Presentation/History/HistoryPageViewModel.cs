using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Activity;
using PCDoc.Core.Findings;
using PCDoc.Core.Fixing;
using PCDoc.Core.Localization;

namespace PCDoc.Presentation.History;

/// <summary>Une modification faite par PCDoc.</summary>
public sealed record HistoryRow(UndoEntry Entry, string Title, string Date, string StatusLabel, bool CanUndo);

/// <summary>Page Historique : toutes les modifications, bouton « Annuler », corrections interrompues signalées.</summary>
public sealed partial class HistoryPageViewModel : ObservableObject
{
    private readonly FixPipeline _pipeline;
    private readonly ILocalizer _localizer;
    private readonly IActivityLog _activity;
    private readonly IReportWriter? _reports;
    private readonly Func<IReadOnlyList<string>, string> _buildReport;
    private string? _lastReport;

    [ObservableProperty]
    private string? _messageText;

    [ObservableProperty]
    private string? _interruptedText;

    [ObservableProperty]
    private bool _isEmpty = true;

    public HistoryPageViewModel(FixPipeline pipeline, ILocalizer localizer, IActivityLog? activity = null, IReportWriter? reports = null,
        Func<IReadOnlyList<string>, string>? buildReport = null)
    {
        _pipeline = pipeline;
        _localizer = localizer;
        _activity = activity ?? new NullActivityLog();
        _reports = reports;
        _buildReport = buildReport ?? (lines => string.Join(Environment.NewLine, lines));
        localizer.PropertyChanged += (_, _) => Refresh();
    }

    public ObservableCollection<HistoryRow> Entries { get; } = [];

    /// <summary>Journal d'activité, le plus récent en premier (500 dernières lignes).</summary>
    public ObservableCollection<string> Activity { get; } = [];

    public bool CanOpenReport => _lastReport is not null;

    [RelayCommand]
    private void ExportReport()
    {
        if (_reports is null)
        {
            return;
        }

        try
        {
            var name = $"Rapport-PCDoc-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            _lastReport = _reports.Save(name, _buildReport(_activity.ReadAll()));
            MessageText = _localizer.Format(new LocalizedText("History.ReportSaved", _lastReport));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageText = _localizer["History.ReportFailed"];
        }

        OnPropertyChanged(nameof(CanOpenReport));
    }

    [RelayCommand]
    private void OpenReportFolder()
    {
        if (_lastReport is not null)
        {
            _reports?.OpenFolder(_lastReport);
        }
    }

    public void Refresh()
    {
        Entries.Clear();
        foreach (var e in _pipeline.Journal.Entries())
        {
            Entries.Add(new HistoryRow(e, _localizer.Format(e.Title),
                e.Timestamp.ToLocalTime().ToString("g", (_localizer as Localizer)?.Culture),
                _localizer[$"History.Status.{e.Status}"], e.Status is UndoStatus.Applied or UndoStatus.Pending && _pipeline.CanUndo(e)));
        }

        var interrupted = _pipeline.Journal.Interrupted();
        InterruptedText = interrupted.Count == 0
            ? null
            : _localizer.Format(new LocalizedText("History.Interrupted", string.Join(", ", interrupted.Select(i => _localizer.Format(i.Title)))));
        IsEmpty = Entries.Count == 0;
        Activity.Clear();
        foreach (var line in _activity.ReadAll().Reverse().Take(500))
        {
            Activity.Add(line);
        }
    }

    [RelayCommand]
    private async Task UndoAsync(HistoryRow? row)
    {
        if (row is null)
        {
            return;
        }

        var outcome = await _pipeline.UndoAsync(row.Entry, CancellationToken.None).ConfigureAwait(true);
        MessageText = _localizer.Format(new LocalizedText($"History.Undo.{outcome}", row.Title));
        Refresh();
    }
}
