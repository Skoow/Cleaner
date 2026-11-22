namespace PCDoc.Core.Abstractions;

/// <summary>Connexion réseau ouverte par un programme (TCP ou UDP).</summary>
public sealed record NetConnection(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string? RemoteAddress,
    int? RemotePort,
    string State,
    int ProcessId,
    string? ProcessName,
    string? ProcessPath);

/// <summary>Connexions et ports en écoute (tables TCP/UDP de Windows, avec le programme propriétaire).</summary>
public interface INetworkConnections
{
    IReadOnlyList<NetConnection> List();
}

/// <summary>Extension d'un navigateur du profil cible.</summary>
public sealed record BrowserExtension(string Browser, string Id, string Name, string? Version, bool? Enabled, string Folder);

/// <summary>Extensions installées dans Chrome, Edge, Brave et Firefox (lecture seule des fichiers du profil).</summary>
public interface IBrowserExtensions
{
    IReadOnlyList<BrowserExtension> List();
}

public enum SecondOpinionTool
{
    /// <summary>Microsoft Safety Scanner (msert.exe), analyse rapide et nettoyage automatique.</summary>
    SafetyScanner,

    /// <summary>Malwarebytes AdwCleaner, logiciels publicitaires et programmes indésirables (analyse seule).</summary>
    AdwCleaner,

    /// <summary>AdwCleaner avec suppression : quarantaine gardée dans %ProgramData%\PCDoc\adwcleaner (restaurable avec AdwCleaner).</summary>
    AdwCleanerClean,

    /// <summary>Kaspersky Virus Removal Tool : virus et chevaux de Troie ; les menaces vont dans sa quarantaine (%ProgramData%\PCDoc\kvrt).</summary>
    KasperskyVrt,
}

/// <param name="LogLines">Dernières lignes du journal de l'outil (aussi copiées dans les rapports).</param>
public sealed record SecondOpinionResult(int ExitCode, IReadOnlyList<string> LogLines);

/// <summary>
/// Outil d'analyse d'un autre éditeur, téléchargé depuis son site officiel, signature vérifiée, lancé sans fenêtre
/// dans le dossier temporaire protégé de PCDoc, puis supprimé.
/// </summary>
public interface ISecondOpinion
{
    Task<SecondOpinionResult> RunAsync(SecondOpinionTool tool, Action<string>? onOutput, CancellationToken cancellationToken);
}

/// <summary>Résultat VirusTotal d'une empreinte SHA-256 (null = fichier inconnu de VirusTotal).</summary>
public sealed record VirusTotalStats(int Malicious, int Suspicious, int Harmless, int Undetected);

/// <summary>Recherche par empreinte uniquement : aucun fichier n'est envoyé.</summary>
public interface IVirusTotal
{
    bool HasKey { get; }

    void SetKey(string? apiKey);

    /// <summary>Null si l'empreinte est inconnue ; exception si la clé est refusée ou le service indisponible.</summary>
    Task<VirusTotalStats?> LookupAsync(string sha256, CancellationToken cancellationToken);
}
