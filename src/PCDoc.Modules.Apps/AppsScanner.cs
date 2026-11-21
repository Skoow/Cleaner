using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Apps;

/// <summary>Bilan : applications préinstallées 🟢 encore présentes → « Info » corrigeables.</summary>
public sealed class AppsScanner(IAppsSource source) : IScanner
{
    public string ModuleId => AppsModule.Id;

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken)
    {
        var report = await source.AnalyzeAsync(cancellationToken).ConfigureAwait(false);
        return [.. report.Apps.Where(a => a.IsBloat && a.CanUninstall && a.Risk == RiskLevel.Safe).Select(UninstallFixer.FindingFor)];
    }
}
