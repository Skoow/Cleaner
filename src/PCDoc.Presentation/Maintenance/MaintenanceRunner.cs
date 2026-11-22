using PCDoc.Core.Activity;
using PCDoc.Core.Findings;
using PCDoc.Core.Fixing;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Modules.Cleanup;
using PCDoc.Modules.Security;
using PCDoc.Modules.Updates;

namespace PCDoc.Presentation.Maintenance;

/// <summary>
/// Entretien automatique, sans fenêtre (lancé par la tâche planifiée avec « --maintenance ») : nettoyage, mises à jour
/// des logiciels et de Windows, analyse rapide Defender. Uniquement des actions 🟢, en mode novice, avec point de
/// restauration ; tout est inscrit au journal et un rapport .txt est enregistré.
/// </summary>
public sealed class MaintenanceRunner(
    ICleanupSource cleanup,
    IUpdatesSource updates,
    ISecuritySource security,
    FixPipeline pipeline,
    IActivityLog activity,
    IReportWriter? reports,
    ILocalizer localizer,
    Func<IReadOnlyList<string>, string> buildReport)
{
    public async Task<FixSessionResult?> RunAsync(MaintenanceOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var firstLine = activity.ReadAll().Count;
        activity.Write("maintenance", localizer["Maintenance.Started"]);
        var findings = new List<Finding>();
        if (options.SoftwareUpdates || options.WindowsUpdates)
        {
            try
            {
                var report = await updates.AnalyzeAsync(options.WindowsUpdates, cancellationToken).ConfigureAwait(false);
                if (options.WindowsUpdates)
                {
                    findings.AddRange(report.Windows.Select(UpdateFixer.ForWindows));
                }

                if (options.SoftwareUpdates)
                {
                    findings.AddRange(report.Software.Select(UpdateFixer.ForUpgrade));
                }
            }
#pragma warning disable CA1031 // une source en échec n'empêche pas le reste de l'entretien
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity.Write("maintenance", localizer.Format(new LocalizedText("Maintenance.SourceFailed", "updates", ex.Message)));
            }
        }

        if (options.Cleanup)
        {
            try
            {
                var report = await Task.Run(cleanup.Analyze, cancellationToken).ConfigureAwait(false);
                findings.AddRange(report.Categories.Where(c => c.Bytes > 0).Select(c => CleanupFixer.FindingFor(c.Id, c.Risk, c.Bytes, c.Files)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity.Write("maintenance", localizer.Format(new LocalizedText("Maintenance.SourceFailed", "cleanup", ex.Message)));
            }
        }

        FixSessionResult? result = null;
        var safe = findings.Where(FixPolicy.IsInFixAll).ToList();
        if (safe.Count > 0)
        {
            result = await pipeline.RunAsync(safe, AppMode.Novice, allowWithoutRestorePoint: false, cancellationToken).ConfigureAwait(false);
            if (result.NeedsConfirmationWithoutRestorePoint)
            {
                activity.Write("maintenance", localizer["Maintenance.NoRestorePoint"]);
            }
        }

        if (options.DefenderScan)
        {
            try
            {
                await security.ScanAsync(1, null, null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                activity.Write("maintenance", localizer.Format(new LocalizedText("Maintenance.SourceFailed", "defender", ex.Message)));
            }
        }

        activity.Write("maintenance", localizer["Maintenance.Finished"]);
        try
        {
            reports?.Save($"Entretien-PCDoc-{DateTime.Now:yyyyMMdd-HHmmss}.txt", buildReport([.. activity.ReadAll().Skip(firstLine)]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Le journal d'activité garde la trace de l'entretien.
        }

        return result;
    }
}
