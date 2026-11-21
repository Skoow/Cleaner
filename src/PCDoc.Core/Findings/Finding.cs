namespace PCDoc.Core.Findings;

/// <summary>Un problème trouvé par une analyse (spec v2 §3.3).</summary>
public sealed record Finding(
    string Id,
    string Module,
    Severity Severity,
    LocalizedText Title,
    LocalizedText Explanation,
    string TechnicalDetails,
    bool FixAvailable = false,
    RiskLevel Risk = RiskLevel.Safe,
    bool Reversible = true,
    string? EstimatedGain = null);
