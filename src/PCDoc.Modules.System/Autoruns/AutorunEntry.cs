using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Autoruns;

/// <summary>Type d'élément lancé automatiquement.</summary>
public enum AutorunKind
{
    Startup,
    ScheduledTask,
    Service,
    Driver,
}

/// <summary>Un élément lancé automatiquement, avec le fichier réellement exécuté et sa signature.</summary>
public sealed record AutorunEntry(
    AutorunKind Kind,
    string Name,
    string Location,
    string CommandLine,
    string? FilePath,
    bool Enabled,
    SignatureInfo Signature,
    bool IsMicrosoft);
