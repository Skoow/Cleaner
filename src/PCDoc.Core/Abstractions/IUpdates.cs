namespace PCDoc.Core.Abstractions;

/// <summary>Mise à jour proposée par Windows Update.</summary>
public sealed record WindowsUpdateItem(
    string Id,
    string Title,
    bool IsDriver,
    bool IsOptional,
    long SizeBytes,
    string? KbArticle = null,
    bool Exclusive = false);

public sealed record WindowsUpdateResult(int Installed, int Failed, bool RebootRequired);

/// <summary>Windows Update par son API officielle (aucune fenêtre).</summary>
public interface IWindowsUpdate
{
    Task<IReadOnlyList<WindowsUpdateItem>> SearchAsync(CancellationToken cancellationToken);

    /// <summary>Recherche de nouveau les mises à jour par identifiant, les télécharge et les installe.</summary>
    Task<WindowsUpdateResult> InstallAsync(IReadOnlyCollection<string> updateIds, CancellationToken cancellationToken);
}

/// <summary>winget.exe du paquet App Installer (dossier WindowsApps, réservé à TrustedInstaller).</summary>
public interface IWingetRunner
{
    /// <summary>Lance winget caché ; renvoie le code de sortie et les lignes écrites. winget absent → <see cref="FileNotFoundException"/>.</summary>
    Task<(int ExitCode, IReadOnlyList<string> Output)> RunAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Écritures de valeurs simples (DWORD, texte) pour les réglages d'optimisation.</summary>
public interface IRegistryValueWriter
{
    /// <param name="value">int (DWORD) ou string (REG_SZ) ; null supprime la valeur.</param>
    void SetValue(RegistryHive hive, string path, string name, object? value);
}
