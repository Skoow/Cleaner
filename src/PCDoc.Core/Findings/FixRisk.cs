namespace PCDoc.Core.Findings;

/// <summary>Risque d'une correction (spec v2 §5 ; les niveaux Déconseillé/Verrouillé arrivent en v0.2).</summary>
public enum FixRisk
{
    Safe,
    Moderate,
    Advanced,
}
