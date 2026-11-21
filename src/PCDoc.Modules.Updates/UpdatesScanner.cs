using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Updates;

/// <summary>Bilan : logiciels à mettre à jour (winget) → « Info » corrigeables. Windows Update est consulté dans sa page.</summary>
public sealed class UpdatesScanner(IUpdatesSource source) : IScanner
{
    public string ModuleId => UpdatesModule.Id;

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken)
    {
        var report = await source.AnalyzeAsync(includeWindows: false, cancellationToken).ConfigureAwait(false);
        return [.. report.Software.Select(UpdateFixer.ForUpgrade)];
    }
}
