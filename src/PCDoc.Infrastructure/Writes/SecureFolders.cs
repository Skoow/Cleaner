using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using PCDoc.Core;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>Le dossier de sauvegarde n'est pas sûr (lien, propriétaire étranger) : les corrections sont désactivées.</summary>
public sealed class UnsafeFolderException : Exception
{
    public UnsafeFolderException(string message)
        : base(message)
    {
    }

    public UnsafeFolderException()
    {
    }

    public UnsafeFolderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Dossiers %ProgramData%\PCDoc\… réservés aux administrateurs et à SYSTEM (héritage coupé, propriétaire
/// Administrateurs). Un utilisateur standard pourrait avoir créé le dossier avant PCDoc, ou l'avoir remplacé par un
/// lien vers un autre dossier : les liens sont supprimés, le propriétaire est vérifié, et en cas de doute on refuse.
/// </summary>
public static class SecureFolders
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppInfo.DataFolderName);

    public static string Backup
    {
        get
        {
            Ensure(Root);
            return Ensure(Path.Combine(Root, "backup"));
        }
    }

    public static string Ensure(string path)
    {
        RemoveIfLink(path);
        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            info.Create(Protected(isDirectory: true));
        }
        else
        {
            info.SetAccessControl(Protected(isDirectory: true));
        }

        Verify(new DirectoryInfo(path).GetAccessControl(), path);
        return path;
    }

    /// <summary>Fichier existant (journal) : lien supprimé, droits remis à ceux du dossier protégé, propriétaire vérifié.</summary>
    public static void SecureFile(string path)
    {
        RemoveIfLink(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return;
        }

        var security = new FileSecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false); // hérite du dossier protégé
        file.SetAccessControl(security);
        var actual = file.GetAccessControl();
        if (actual.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !(owner == Administrators || owner == LocalSystem))
        {
            throw new UnsafeFolderException($"{path} is not owned by Administrators");
        }
    }

    private static void RemoveIfLink(string path)
    {
        var attributes = File.Exists(path) || Directory.Exists(path) ? File.GetAttributes(path) : (FileAttributes?)null;
        if (attributes is { } a && a.HasFlag(FileAttributes.ReparsePoint))
        {
            // Supprime le lien lui-même, jamais sa cible.
            if (a.HasFlag(FileAttributes.Directory))
            {
                Directory.Delete(path, recursive: false);
            }
            else
            {
                File.Delete(path);
            }
        }
    }

    private static DirectorySecurity Protected(bool isDirectory)
    {
        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inheritance = isDirectory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        foreach (var sid in new[] { Administrators, LocalSystem })
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None,
                AccessControlType.Allow));
        }

        return security;
    }

    private static void Verify(DirectorySecurity security, string path)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !(owner == Administrators || owner == LocalSystem)
            || !security.AreAccessRulesProtected
            || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new UnsafeFolderException($"{path} is not a protected folder");
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference is SecurityIdentifier sid
                && sid != Administrators && sid != LocalSystem)
            {
                throw new UnsafeFolderException($"{path} grants access to {sid}");
            }
        }
    }
}

/// <summary>Sauvegardes .reg (UTF-16, format de l'Éditeur du Registre) dans le dossier protégé.</summary>
public sealed class BackupStore : IBackupStore
{
    public string SaveRegistryBackup(string name, string regFileContent)
    {
        var path = Path.Combine(SecureFolders.Backup, Path.GetFileName(name));
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            File.Delete(path);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, Encoding.Unicode);
        writer.Write(regFileContent);
        return path;
    }
}
