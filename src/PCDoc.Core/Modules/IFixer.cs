using PCDoc.Core.Findings;

namespace PCDoc.Core.Modules;

/// <summary>
/// Correction réversible. Le pipeline appelle <see cref="PrepareAsync"/> (lecture de l'état d'origine, aucune
/// modification), écrit l'entrée d'annulation sur le disque, puis seulement <see cref="ApplyAsync"/>.
/// </summary>
public interface IFixer
{
    /// <summary>Type des entrées d'annulation produites (pour retrouver le correcteur qui sait annuler).</summary>
    string UndoKind { get; }

    bool CanFix(Finding finding);

    Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken);

    Task ApplyAsync(Finding finding, CancellationToken cancellationToken);

    /// <summary>Vrai si l'élément est toujours dans l'état laissé par la correction (sinon, ne pas annuler à l'aveugle).</summary>
    Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken);

    Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken);
}
