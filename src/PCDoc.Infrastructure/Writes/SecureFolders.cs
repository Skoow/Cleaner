using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using PCDoc.Core;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Dossiers %ProgramData%\PCDoc\… réservés aux administrateurs et à SYSTEM (héritage coupé) : un programme lancé
/// sans droits admin ne peut ni y lire les sauvegardes ni y déposer un fichier (spec v2 §3.4).
/// </summary>
public static class SecureFolders
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppInfo.DataFolderName);

    public static string Backup => Ensure(Path.Combine(Root, "backup"));

    public static string Ensure(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            info.Create(security);
        }
        else
        {
            info.SetAccessControl(security);
        }

        return path;
    }
}

/// <summary>Sauvegardes .reg (UTF-16, format de l'Éditeur du Registre) dans le dossier protégé.</summary>
public sealed class BackupStore : IBackupStore
{
    public string SaveRegistryBackup(string name, string regFileContent)
    {
        var path = Path.Combine(SecureFolders.Backup, Path.GetFileName(name));
        File.WriteAllText(path, regFileContent, Encoding.Unicode);
        return path;
    }
}
