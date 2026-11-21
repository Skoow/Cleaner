using PCDoc.Core.Settings;

namespace PCDoc.Core.Findings;

/// <summary>Ce qui est proposé selon le mode (novice / expert) et le niveau de risque.</summary>
public static class FixPolicy
{
    public static bool IsOffered(Finding finding, AppMode mode)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.FixAvailable && finding.Risk switch
        {
            RiskLevel.Safe => true,
            RiskLevel.Conditional or RiskLevel.Discouraged => mode == AppMode.Expert,
            _ => false,
        };
    }

    /// <summary>« Tout corriger » : 🟢 uniquement, dans les deux modes.</summary>
    public static bool IsInFixAll(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.FixAvailable && finding.Risk == RiskLevel.Safe;
    }

    public static bool NeedsStrongConfirmation(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.Risk == RiskLevel.Discouraged || !finding.Reversible;
    }
}
