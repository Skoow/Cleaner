using PCDoc.Core.Findings;

namespace PCDoc.Core.Modules;

/// <summary>Correction d'un problème, appliquée seulement après confirmation de l'utilisateur.</summary>
public interface IFixer
{
    bool CanFix(Finding finding);

    Task<FixResult> FixAsync(Finding finding, CancellationToken cancellationToken);
}
