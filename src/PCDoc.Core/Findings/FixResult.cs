namespace PCDoc.Core.Findings;

/// <summary>Résultat d'une correction, avec de quoi l'annuler.</summary>
public sealed record FixResult(bool Success, LocalizedText Message, IReadOnlyList<UndoEntry> UndoEntries);
