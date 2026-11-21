namespace PCDoc.Core.Abstractions;

public enum AppKind
{
    Win32,
    Store,
}

public enum AppScope
{
    Machine,
    User,
}

/// <summary>
/// Programme installé. <paramref name="Id"/> : « win32:&lt;vue&gt;:&lt;ruche&gt;:&lt;clé&gt; » ou « store:&lt;famille&gt; ».
/// <paramref name="RegistryPath"/> : clé Uninstall (Win32). <paramref name="ProvisionedName"/> : paquet provisionné (Store).
/// </summary>
public sealed record InstalledApp(
    string Id,
    AppKind Kind,
    AppScope Scope,
    string Name,
    string? Publisher = null,
    string? Version = null,
    long SizeBytes = 0,
    DateOnly? InstallDate = null,
    string? UninstallString = null,
    string? QuietUninstallString = null,
    string? InstallLocation = null,
    string? RegistryPath = null,
    RegistryView View = RegistryView.Default,
    string? ProductCode = null,
    string? PackageFullName = null,
    string? PackageFamilyName = null,
    string? ProvisionedName = null,
    bool IsFramework = false,
    bool NonRemovable = false);

/// <summary>Paquet du Store tel que listé par PowerShell (nom affiché résolu à part).</summary>
public sealed record StorePackage(
    string Name,
    string FullName,
    string FamilyName,
    string? Publisher,
    string? Version,
    string? InstallLocation,
    bool IsFramework,
    bool NonRemovable,
    bool IsSystemSigned);

/// <summary>Nom affiché d'une application du Store (lu dans son AppxManifest.xml).</summary>
public interface IAppDisplayNames
{
    string? Resolve(StorePackage package);
}

/// <summary>Lance un désinstalleur d'éditeur (Program Files ou Windows uniquement, jamais un lien).</summary>
public interface IUninstallLauncher
{
    /// <param name="hidden">Vrai : sans fenêtre (mode silencieux) ; faux : la fenêtre de l'éditeur s'ouvre.</param>
    Task<int> RunAsync(string executable, string arguments, bool hidden, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Vrai si le chemin est un exécutable existant dans un dossier réservé aux administrateurs.</summary>
    bool IsTrusted(string executable);

    /// <summary>Vrai si chaque chemin cité dans les arguments est lui aussi dans un dossier réservé aux administrateurs.</summary>
    bool AreArgumentsTrusted(string arguments);
}

/// <summary>Quarantaine des restes : déplacement réversible dans le dossier protégé, purge après quelques jours.</summary>
public interface IQuarantine
{
    /// <summary>Dossier qui recevra <paramref name="originalPath"/> (calculé sans rien déplacer).</summary>
    string PlanFor(string originalPath);

    void Move(string originalPath, string quarantinePath);

    void Restore(string quarantinePath, string originalPath);

    bool DirectoryExists(string path);

    /// <summary>Vrai si le dossier est sous Program Files (sans être sa racine ni un dossier partagé) et n'est pas un lien.</summary>
    bool IsLeftoverCandidate(string path);

    void PurgeOlderThan(TimeSpan age);
}
