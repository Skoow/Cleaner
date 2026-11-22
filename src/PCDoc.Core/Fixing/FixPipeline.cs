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
public sealed class FixPipeline(IEnumerable<IFixer> fixers, IRestorePointService restorePoints, UndoJournal journal)
{
    private readonly IReadOnlyList<IFixer> _fixers = [.. fixers];

    public UndoJournal Journal => journal;

    /// <summary>Journal lisible (date et heure de chaque action) ; désactivé par défaut.</summary>
    public Activity.IActivityLog Activity { get; set; } = new Activity.NullActivityLog();

    /// <summary>Texte d'un problème dans la langue de l'utilisateur (titre).</summary>
    public Func<LocalizedText, string> Describe { get; set; } = t => t.Key;

    public async Task<FixSessionResult> RunAsync(
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

        var restore = await restorePoints.EnsureAsync("PCDoc", cancellationToken).ConfigureAwait(false);
        if (restore is RestorePointOutcome.ProtectionDisabled or RestorePointOutcome.Failed && !allowWithoutRestorePoint)
        {
            return new FixSessionResult(restore, true, []);
        }

        Activity.Write("restore", Describe(new LocalizedText($"Activity.Restore.{restore}")));

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
