using PCDoc.Core.Findings;

namespace PCDoc.Core.Bilan;

/// <summary>Note globale sur 100 : 100 − 20 par critique − 5 par attention, bornée à [0, 100].</summary>
public static class HealthScore
{
    public const int CriticalPenalty = 20;
    public const int WarningPenalty = 5;

    public static int Compute(IEnumerable<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var penalty = 0;
        foreach (var finding in findings)
        {
            penalty += finding.Severity switch
            {
                Severity.Critical => CriticalPenalty,
                Severity.Warning => WarningPenalty,
                _ => 0,
            };
        }

        return Math.Clamp(100 - penalty, 0, 100);
    }

    /// <summary>Clé du libellé : ≥ 80 « Bon état », 50–79 « À surveiller », &lt; 50 « À améliorer ».</summary>
    public static string LabelKey(int score) => score switch
    {
        >= 80 => "Score.Good",
        >= 50 => "Score.Watch",
        _ => "Score.Improve",
    };
}
