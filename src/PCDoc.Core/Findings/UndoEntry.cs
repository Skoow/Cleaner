namespace PCDoc.Core.Findings;

/// <summary>Une entrée du journal d'annulation.</summary>
public sealed record UndoEntry(string FindingId, DateTimeOffset AppliedAt, string Kind, string Payload);
