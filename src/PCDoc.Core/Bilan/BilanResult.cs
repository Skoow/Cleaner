using PCDoc.Core.Findings;

namespace PCDoc.Core.Bilan;

/// <summary>Résultat complet d'un Bilan.</summary>
public sealed record BilanResult(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<ModuleError> Errors,
    int Score,
    DateTimeOffset CompletedAt);
