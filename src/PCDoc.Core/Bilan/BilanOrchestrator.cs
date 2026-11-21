using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Core.Bilan;

/// <summary>
/// Lance l'analyse de tous les modules en parallèle. Un module qui plante ou dépasse le délai
/// devient une <see cref="ModuleError"/> : le Bilan se termine toujours.
/// </summary>
public sealed class BilanOrchestrator
{
    public static readonly TimeSpan DefaultPerModuleTimeout = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<IScanner> _scanners;
    private readonly IClock _clock;
    private readonly TimeSpan _perModuleTimeout;

    public BilanOrchestrator(IEnumerable<IScanner> scanners, IClock clock, TimeSpan perModuleTimeout)
    {
        ArgumentNullException.ThrowIfNull(scanners);
        ArgumentNullException.ThrowIfNull(clock);
        _scanners = [.. scanners];
        _clock = clock;
        _perModuleTimeout = perModuleTimeout;
    }

    public async Task<BilanResult> RunAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var outcomes = await Task.WhenAll(_scanners.Select(s => RunOneAsync(s, progress, cancellationToken)))
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var findings = outcomes.SelectMany(o => o.Findings)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Module, StringComparer.Ordinal)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();
        var errors = outcomes.Where(o => o.Error is not null).Select(o => o.Error!).ToList();

        return new BilanResult(findings, errors, HealthScore.Compute(findings), _clock.Now);
    }

    private async Task<(IReadOnlyList<Finding> Findings, ModuleError? Error)> RunOneAsync(
        IScanner scanner, IProgress<string>? progress, CancellationToken userToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(userToken);
        timeout.CancelAfter(_perModuleTimeout);
        try
        {
            // Task.Run : un scanner qui bloque de façon synchrone ne bloque pas les autres.
            var scan = Task.Run(() => scanner.ScanAsync(timeout.Token), timeout.Token);
            var findings = await scan.WaitAsync(timeout.Token).ConfigureAwait(false);
            return (findings, null);
        }
        catch (OperationCanceledException) when (userToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ([], new ModuleError(scanner.ModuleId, $"Timed out after {_perModuleTimeout.TotalSeconds:0.#} s"));
        }
#pragma warning disable CA1031 // Un module en panne ne doit jamais faire échouer le Bilan.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return ([], new ModuleError(scanner.ModuleId, ex.Message));
        }
        finally
        {
            progress?.Report(scanner.ModuleId);
        }
    }
}
