using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;
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

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken)
    {
        var inventoryTask = Task.Run(inventory.Collect, cancellationToken);
        var bootTask = Task.Run(() => boot.Read(), cancellationToken);
        var autorunTask = Task.Run(autoruns.Collect, cancellationToken);

        var findings = new List<Finding>();
        var pc = await Try("inventory", inventoryTask).ConfigureAwait(false);
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

        if (await Try("boot", bootTask).ConfigureAwait(false) is { } history)
        {
            findings.AddRange(BootTimeRule.Evaluate(history));
        }

        if (await Try("autoruns", autorunTask).ConfigureAwait(false) is { } entries)
        {
            findings.AddRange(AutorunSignatureRule.Evaluate(entries));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Deux éléments du même nom donneraient le même identifiant : on garde le premier.
        return [.. findings.DistinctBy(f => f.Id)];
    }

    private async Task<T?> Try<T>(string source, Task<T> task)
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
            return null;
        }
    }
}
