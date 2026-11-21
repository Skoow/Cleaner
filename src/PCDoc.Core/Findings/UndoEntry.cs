namespace PCDoc.Core.Findings;

/// <summary>État d'une entrée du journal d'annulation.</summary>
public enum UndoStatus
{
    /// <summary>Écrite avant la modification ; si elle reste ainsi, la correction a été interrompue.</summary>
    Pending,
    Applied,
    Failed,
    Undone,
}

/// <summary>
/// Une correction et de quoi l'annuler. <paramref name="Payload"/> : état d'origine (JSON propre au correcteur).
/// <paramref name="TitleKey"/>/<paramref name="TitleArgs"/> : texte traduit affiché dans l'Historique.
/// </summary>
public sealed record UndoEntry(
    string Id,
    string FindingId,
    string Kind,
    string Payload,
    string TitleKey,
    IReadOnlyList<string> TitleArgs,
    DateTimeOffset Timestamp,
    UndoStatus Status)
{
    public static UndoEntry Create(string findingId, string kind, string payload, string titleKey, IReadOnlyList<string> titleArgs) =>
        new(Guid.NewGuid().ToString("N"), findingId, kind, payload, titleKey, titleArgs, default, UndoStatus.Pending);

    public LocalizedText Title => new(TitleKey, [.. TitleArgs]);
}
