namespace PCDoc.Core.Abstractions;

/// <summary>Type de démarrage d'un service ou d'un pilote.</summary>
public enum ServiceStartMode
{
    Boot,
    System,
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
    Unknown,
}

/// <summary>Un service Windows ou un pilote (<paramref name="IsDriver"/>).</summary>
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    ServiceStartMode StartMode,
    string State,
    string? ImagePath,
    string? Description,
    bool IsDriver);

/// <summary>Lecture des services et des pilotes.</summary>
public interface IServiceReader
{
    IReadOnlyList<ServiceInfo> ReadAll();
}
