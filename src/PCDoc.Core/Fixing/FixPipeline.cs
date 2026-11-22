using PCDoc.Core.Findings;
using PCDoc.Core.Modules;
using PCDoc.Core.Settings;

namespace PCDoc.Core.Fixing;

public enum FixStatus
{
    Applied,
    Failed,
    NotAllowed,
    NoFixer,
}

public enum UndoOutcome
{
    Undone,

    /// <summary>L'élément a changé depuis la correction (mise à jour de Windows…) : on n'annule pas à l'aveugle.</summary>
    StateChanged,

    NoFixer,
    Failed,
}

public sealed record FixOutcome(Finding Finding, FixStatus Status, string? Error = null);

/// <param name="NeedsConfirmationWithoutRestorePoint">Aucun point de restauration possible : rien n'a été modifié,
/// l'utilisateur doit confirmer de nouveau (spec v2 §4.3).</param>
public sealed record FixSessionResult(
    RestorePointOutcome? RestorePoint, bool NeedsConfirmationWithoutRestorePoint, IReadOnlyList<FixOutcome> Outcomes);

/// <summary>
/// Applique des corrections déjà confirmées par l'utilisateur, de façon sûre : mode et niveau de risque respectés,
/// un point de restauration par série, entrée d'annulation écrite avant chaque modification.
/// </summary>
public sealed class FixPipeline(IEnumerable<IFixer> fixers, IRestorePointService restorePoints, UndoJournal journal) : IDisposable
{
    private readonly IReadOnlyList<IFixer> _fixers = [.. fixers];

    // Une seule série à la fois dans toute l'application (Windows Update, MSI et DISM n'acceptent qu'une installation à la fois).
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Vrai pendant une série de corrections ou une annulation.</summary>
    public bool IsRunning => _gate.CurrentCount == 0;

    /// <summary>Après une série qui a appliqué au moins une correction : modules concernés (les pages se rafraîchissent).</summary>
    public event EventHandler<IReadOnlyCollection<string>>? SessionCompleted;

    public UndoJournal Journal => journal;

    public void Dispose() => _gate.Dispose();

    /// <summary>Journal lisible (date et heure de chaque action) ; désactivé par défaut.</summary>
    public Activity.IActivityLog Activity { get; set; } = new Activity.NullActivityLog();

    /// <summary>Texte d'un problème dans la langue de l'utilisateur (titre).</summary>
    public Func<LocalizedText, string> Describe { get; set; } = t => t.Key;

    public async Task<FixSessionResult> RunAsync(
        IReadOnlyList<Finding> findings, AppMode mode, bool allowWithoutRestorePoint, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await RunLockedAsync(findings, mode, allowWithoutRestorePoint, cancellationToken).ConfigureAwait(false);
            var modules = result.Outcomes.Where(o => o.Status == FixStatus.Applied).Select(o => o.Finding.Module).ToHashSet(StringComparer.Ordinal);
            if (modules.Count > 0)
            {
                SessionCompleted?.Invoke(this, modules);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FixSessionResult> RunLockedAsync(
        IReadOnlyList<Finding> findings, AppMode mode, bool allowWithoutRestorePoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var outcomes = new List<FixOutcome>();
        var allowed = new List<(Finding Finding, IFixer Fixer)>();
        foreach (var finding in findings)
        {
            var fixer = _fixers.FirstOrDefault(f => SafeCanFix(f, finding));
            if (!FixPolicy.IsOffered(finding, mode))
            {
                outcomes.Add(new FixOutcome(finding, FixStatus.NotAllowed));
            }
            else if (fixer is null)
            {
                outcomes.Add(new FixOutcome(finding, FixStatus.NoFixer));
            }
            else
            {
                allowed.Add((finding, fixer));
            }
        }

        if (allowed.Count == 0)
        {
            return new FixSessionResult(null, false, Ordered(findings, outcomes));
        }

        // Pas de point de restauration pour un effacement définitif seul : le cliché du disque garderait une copie
        // des fichiers que l'on veut justement faire disparaître.
        RestorePointOutcome? restore = null;
        if (allowed.Any(a => a.Fixer.NeedsRestorePoint))
        {
            restore = await restorePoints.EnsureAsync("PCDoc", cancellationToken).ConfigureAwait(false);
            if (restore is RestorePointOutcome.ProtectionDisabled or RestorePointOutcome.Failed && !allowWithoutRestorePoint)
            {
                return new FixSessionResult(restore, true, []);
            }

            Activity.Write("restore", Describe(new LocalizedText($"Activity.Restore.{restore}")));
        }

        foreach (var (finding, fixer) in allowed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UndoEntry? entry = null;
            try
            {
                entry = journal.Begin(await fixer.PrepareAsync(finding, cancellationToken).ConfigureAwait(false));
                await fixer.ApplyAsync(finding, cancellationToken).ConfigureAwait(false);
                journal.Mark(entry.Id, UndoStatus.Applied);
                outcomes.Add(new FixOutcome(finding, FixStatus.Applied));
                Activity.Write(finding.Module, Describe(new LocalizedText("Activity.Applied", Describe(finding.Title), finding.TechnicalDetails)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Une correction qui échoue n'arrête pas les autres ; elle est journalisée.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                if (entry is not null)
                {
                    journal.Mark(entry.Id, UndoStatus.Failed);
                }

                outcomes.Add(new FixOutcome(finding, FixStatus.Failed, ex.Message));
                Activity.Write(finding.Module, Describe(new LocalizedText("Activity.Failed", Describe(finding.Title), ex.Message)));
            }
        }

        return new FixSessionResult(restore, false, Ordered(findings, outcomes));
    }

    /// <summary>
    /// Au démarrage : une réparation non annulable restée « en attente » (coupure pendant SFC…) est marquée « échec »,
    /// pour ne pas signaler indéfiniment une modification que personne ne peut annuler.
    /// </summary>
    public void ResolveInterrupted()
    {
        foreach (var entry in journal.Interrupted().Where(e => !CanUndo(e)))
        {
            journal.Mark(entry.Id, UndoStatus.Failed);
        }
    }

    /// <summary>Vrai si un correcteur sait annuler ce type d'entrée.</summary>
    public bool CanUndo(UndoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _fixers.FirstOrDefault(f => f.UndoKind == entry.Kind) is { SupportsUndo: true };
    }

    public async Task<UndoOutcome> UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await UndoLockedAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UndoOutcome> UndoLockedAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var fixer = _fixers.FirstOrDefault(f => f.UndoKind == entry.Kind);
        if (fixer is not { SupportsUndo: true })
        {
            return UndoOutcome.NoFixer;
        }

        try
        {
            if (!await fixer.IsUnchangedSinceAsync(entry, cancellationToken).ConfigureAwait(false))
            {
                return UndoOutcome.StateChanged;
            }

            await fixer.UndoAsync(entry, cancellationToken).ConfigureAwait(false);
            journal.Mark(entry.Id, UndoStatus.Undone);
            Activity.Write("undo", Describe(new LocalizedText("Activity.Undone", Describe(entry.Title))));
            return UndoOutcome.Undone;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            return UndoOutcome.Failed;
        }
    }

    /// <summary>Un correcteur qui lève une exception sur un problème qui ne le concerne pas ne bloque pas la série.</summary>
    private static bool SafeCanFix(IFixer fixer, Finding finding)
    {
        try
        {
            return fixer.CanFix(finding);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private static List<FixOutcome> Ordered(IReadOnlyList<Finding> findings, List<FixOutcome> outcomes) =>
        [.. findings.Select(f => outcomes.First(o => ReferenceEquals(o.Finding, f)))];
}
