using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty]
    private string? _messageText;

    [ObservableProperty]
    private string? _interruptedText;

    [ObservableProperty]
    private bool _isEmpty = true;

    public HistoryPageViewModel(FixPipeline pipeline, ILocalizer localizer)
    {
        _pipeline = pipeline;
        _localizer = localizer;
        localizer.PropertyChanged += (_, _) => Refresh();
    }

    public ObservableCollection<HistoryRow> Entries { get; } = [];

    public void Refresh()
    {
        Entries.Clear();
        foreach (var e in _pipeline.Journal.Entries())
        {
            Entries.Add(new HistoryRow(e, _localizer.Format(e.Title),
                e.Timestamp.ToLocalTime().ToString("g", (_localizer as Localizer)?.Culture),
                _localizer[$"History.Status.{e.Status}"], e.Status == UndoStatus.Applied));
        }

        var interrupted = _pipeline.Journal.Interrupted();
        InterruptedText = interrupted.Count == 0
            ? null
            : _localizer.Format(new LocalizedText("History.Interrupted", string.Join(", ", interrupted.Select(i => _localizer.Format(i.Title)))));
        IsEmpty = Entries.Count == 0;
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
