using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Findings;
using PCDoc.Core.Fixing;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;

namespace PCDoc.Presentation.Fixing;

/// <summary>Une correction à confirmer : titre traduit, niveau de risque.</summary>
public sealed record FixItem(string Id, string Title, RiskLevel Risk, string RiskLabel);

/// <summary>
/// Panneau de confirmation dans la page (jamais de boîte de dialogue) : liste exacte de ce qui va changer,
/// niveau de risque, case « Je comprends le risque » pour 🔴, seconde confirmation sans point de restauration.
/// </summary>
#pragma warning disable CA1001 // _run est libéré par le « using » de RunAsync
public sealed partial class FixConfirmationViewModel : ObservableObject
{
    private readonly FixPipeline _pipeline;
    private readonly ILocalizer _localizer;
    private readonly Func<AppMode> _mode;
    private IReadOnlyList<Finding> _pending = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmWithoutRestorePointCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _riskAcknowledged;

    [ObservableProperty]
    private bool _needsStrongConfirmation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmWithoutRestorePointCommand))]
    private bool _awaitingNoRestorePointConfirmation;

    [ObservableProperty]
    private string? _restorePointText;

    [ObservableProperty]
    private string? _resultText;

    public FixConfirmationViewModel(FixPipeline pipeline, ILocalizer localizer, Func<AppMode> mode)
    {
        _pipeline = pipeline;
        _localizer = localizer;
        _mode = mode;
    }

    /// <summary>Après des corrections appliquées (la page relance son analyse).</summary>
    public event EventHandler? Completed;

    public ObservableCollection<FixItem> Items { get; } = [];

    public void Request(IEnumerable<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (IsRunning || AwaitingNoRestorePointConfirmation)
        {
            // Une série est en cours ou attend une confirmation : jamais remplacer ce que l'utilisateur a confirmé.
            return;
        }

        var mode = _mode();
        _pending = [.. findings.Where(f => FixPolicy.IsOffered(f, mode))];
        Items.Clear();
        foreach (var f in _pending)
        {
            Items.Add(new FixItem(f.Id, _localizer.Format(f.Title), f.Risk, _localizer[$"Risk.{f.Risk}"]));
        }

        NeedsStrongConfirmation = _pending.Any(FixPolicy.NeedsStrongConfirmation);
        RiskAcknowledged = false;
        AwaitingNoRestorePointConfirmation = false;
        RestorePointText = null;
        ResultText = null;
        IsOpen = _pending.Count > 0;
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private bool CanConfirm() => IsOpen && !IsRunning && (!NeedsStrongConfirmation || RiskAcknowledged);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmAsync() => RunAsync(allowWithoutRestorePoint: false);

    private bool CanConfirmWithoutRestorePoint() =>
        AwaitingNoRestorePointConfirmation && !IsRunning && (!NeedsStrongConfirmation || RiskAcknowledged);

    [RelayCommand(CanExecute = nameof(CanConfirmWithoutRestorePoint))]
    private Task ConfirmWithoutRestorePointAsync() => RunAsync(allowWithoutRestorePoint: true);

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        AwaitingNoRestorePointConfirmation = false;
        _pending = [];
        Items.Clear();
    }

    private CancellationTokenSource? _run;

    /// <summary>Arrête la série après l'action en cours (les analyses longues s'arrêtent aussitôt).</summary>
    [RelayCommand]
    private void StopRun() => _run?.Cancel();

    private async Task RunAsync(bool allowWithoutRestorePoint)
    {
        IsRunning = true;
        using var run = _run = new CancellationTokenSource();
        try
        {
            var result = await _pipeline.RunAsync(_pending, _mode(), allowWithoutRestorePoint, run.Token).ConfigureAwait(true);
            if (result.NeedsConfirmationWithoutRestorePoint)
            {
                AwaitingNoRestorePointConfirmation = true;
                RestorePointText = _localizer[$"Fix.RestorePoint.{result.RestorePoint}"];
                return;
            }

            var applied = result.Outcomes.Count(o => o.Status == FixStatus.Applied);
            var failed = result.Outcomes.Count(o => o.Status == FixStatus.Failed);
            ResultText = _localizer.Format(new LocalizedText(failed == 0 ? "Fix.Result" : "Fix.ResultWithFailures", applied, failed));
            RestorePointText = result.RestorePoint is { } rp ? _localizer[$"Fix.RestorePoint.{rp}"] : null;
            AwaitingNoRestorePointConfirmation = false;
            IsOpen = false;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            ResultText = _localizer["Fix.Stopped"];
            IsOpen = false;
            AwaitingNoRestorePointConfirmation = false;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _run = null;
            IsRunning = false;
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }
}
#pragma warning restore CA1001
