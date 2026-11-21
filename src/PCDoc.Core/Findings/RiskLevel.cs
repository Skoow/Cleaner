namespace PCDoc.Core.Findings;

/// <summary>Risque d'une correction (spec v2 §5).</summary>
public enum RiskLevel
{
    /// <summary>🟢 Ne casse rien.</summary>
    Safe,

    /// <summary>🟠 Sûr si une condition est vraie (le module la vérifie ; vérifiée → Safe).</summary>
    Conditional,

    /// <summary>🔴 Peut gêner ou affaiblir la sécurité : mode expert, une par une.</summary>
    Discouraged,

    /// <summary>🔒 Jamais, même en mode expert.</summary>
    Locked,
}
