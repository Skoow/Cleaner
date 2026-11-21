using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Cleanup;

/// <summary>Bilan : catégorie de plus de 100 Mo → « Info » corrigeable ; corbeille de plus de 1 Go → « Info » 🟠.</summary>
public sealed class CleanupScanner(ICleanupSource source) : IScanner
{
    private const long CategoryThreshold = 100L * 1024 * 1024;
    private const long RecycleBinThreshold = 1024L * 1024 * 1024;

    public string ModuleId => CleanupModule.Id;

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken)
    {
        var report = await Task.Run(source.Analyze, cancellationToken).ConfigureAwait(false);
        var findings = report.Categories
            .Where(c => c.Bytes >= CategoryThreshold)
            .Select(c => CleanupFixer.FindingFor(c.Id, c.Risk, c.Bytes, c.Files))
            .ToList();
        if (report.RecycleBinBytes >= RecycleBinThreshold)
        {
            findings.Add(CleanupFixer.FindingFor(CleanupEngine.RecycleBinId, RiskLevel.Conditional, report.RecycleBinBytes, (int)report.RecycleBinItems));
        }

        return findings;
    }
}
