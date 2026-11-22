using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Optimize;

/// <summary>Bilan : réglages 🟢 marqués « bilan » pas encore appliqués → « Info » corrigeables.</summary>
public sealed class OptimizeScanner(IOptimizeSource source, string moduleId = OptimizeModule.Id) : IScanner
{
    public string ModuleId => moduleId;

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken)
    {
        var states = await source.AnalyzeAsync(cancellationToken).ConfigureAwait(false);
        return [.. states.Where(s => s.Tweak.InBilan && s.Available && !s.Applied && s.Risk == RiskLevel.Safe).Select(TweakFixer.FindingFor)];
    }
}
