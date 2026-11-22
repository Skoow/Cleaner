using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Security;

/// <summary>Bilan : tous les problèmes de sécurité trouvés (antivirus, pare-feu, cachettes, réglages, clés USB).</summary>
public sealed class SecurityScanner(ISecuritySource source) : IScanner
{
    public string ModuleId => SecurityModule.Id;

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken)
    {
        var report = await source.AnalyzeAsync(cancellationToken).ConfigureAwait(false);
        return [.. report.Findings.Where(f => f.Severity != Severity.Info || f.FixAvailable)];
    }
}
