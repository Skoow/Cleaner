using PCDoc.Core.Findings;

namespace PCDoc.Core.Modules;

/// <summary>Analyse d'un module. Lecture seule, jamais d'effet de bord.</summary>
public interface IScanner
{
    string ModuleId { get; }

    Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken);
}
