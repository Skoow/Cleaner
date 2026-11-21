using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;
using PCDoc.Modules.System.Advisor;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Boot;
using PCDoc.Modules.System.Inventory;
using PCDoc.Modules.System.Rules;

namespace PCDoc.Modules.System;

/// <summary>
/// Analyse du module Système pour le Bilan (lecture seule). Les trois collectes tournent en parallèle ;
/// une collecte qui échoue est signalée à <paramref name="onSourceError"/> et ses règles sont ignorées.
/// </summary>
public sealed class SystemScanner(
    IInventorySource inventory,
    IBootSource boot,
    IAutorunSource autoruns,
    IClock clock,
    Action<string, Exception>? onSourceError = null) : IScanner
{
    public string ModuleId => SystemModule.Id;

    /// <summary>Conseiller : rend corrigeables les éléments sûrs (null = aucune correction proposée).</summary>
    public AutorunAdvisor? Advisor { get; init; }

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken)
    {
        var inventoryTask = Task.Run(inventory.Collect, cancellationToken);
        var bootTask = Task.Run(() => boot.Read(), cancellationToken);
        var autorunTask = Task.Run(autoruns.CollectAll, cancellationToken);

        var findings = new List<Finding>();
        var failed = new List<string>();
        var pc = await Try("inventory", inventoryTask, failed).ConfigureAwait(false);
        if (pc is not null)
        {
            if (pc.Volumes.Value is { } volumes)
            {
                findings.AddRange(DiskSpaceRule.Evaluate(volumes));
            }

            if (pc.Windows.Value is { } windows)
            {
                findings.AddRange(WindowsRules.Evaluate(windows, pc.Win11.Value, clock.Now));
            }

            if (pc.Batteries.Value is { } batteries)
            {
                findings.AddRange(BatteryWearRule.Evaluate(batteries));
            }
        }

        if (await Try("boot", bootTask, failed).ConfigureAwait(false) is { } history)
        {
            findings.AddRange(BootTimeRule.Evaluate(history));
        }

        if (await Try("autoruns", autorunTask, failed).ConfigureAwait(false) is { } collection)
        {
            findings.AddRange(AutorunSignatureRule.Evaluate(collection.Entries, Advisor));
            if (Advisor is not null)
            {
                findings.AddRange(AdviceRule.Evaluate(collection.Entries, Advisor));
            }

            failed.AddRange(collection.FailedSources.Select(s => "autoruns." + s));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Deux éléments du même nom donneraient le même identifiant : on garde le premier.
        IReadOnlyList<Finding> result = [.. findings.DistinctBy(f => f.Id)];
        return failed.Count == 0 ? result : throw new PartialScanException(result, failed);
    }

    private async Task<T?> Try<T>(string source, Task<T> task, List<string> failed)
        where T : class
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Une source en panne ne doit pas faire échouer tout le module.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            onSourceError?.Invoke(source, ex);
            failed.Add(source);
            return null;
        }
    }
}
