using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Autoruns;

/// <summary>Un programme lancé à l'ouverture de session (clé Run/RunOnce ou dossier Démarrage).</summary>
public sealed record StartupItem(string Name, string Location, string CommandLine, bool Enabled);

/// <summary>
/// Programmes au démarrage. « HKCU » = ruche du profil cible (jamais celle du compte qui exécute PCDoc).
/// État activé : Explorer\StartupApproved (octet 0 impair = désactivé ; valeur absente = activé).
/// </summary>
public sealed class StartupCollector(IRegistryReader registry, IFileProbe files, IUserProfileResolver profiles)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Wow64RunKey = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string StartupFolderRelative = @"Microsoft\Windows\Start Menu\Programs\Startup";
    private const int MaxLnkSize = 64 * 1024;

    public IReadOnlyList<StartupItem> Collect()
    {
        var items = new List<StartupItem>();
        ReadRunKey(items, RegistryHive.LocalMachine, "HKLM", RunKey, "Run");
        ReadRunKey(items, RegistryHive.LocalMachine, "HKLM", RunOnceKey, null);
        ReadRunKey(items, RegistryHive.LocalMachine, "HKLM", Wow64RunKey, "Run32");
        ReadRunKey(items, RegistryHive.TargetUser, "HKCU", RunKey, "Run");
        ReadRunKey(items, RegistryHive.TargetUser, "HKCU", RunOnceKey, null);

        var common = WinPath.Combine(files.ExpandEnvironment("%ProgramData%"), StartupFolderRelative);
        var user = WinPath.Combine(profiles.Resolve().RoamingAppData, StartupFolderRelative);
        ReadFolder(items, common, RegistryHive.LocalMachine);
        ReadFolder(items, user, RegistryHive.TargetUser);
        return items;
    }

    private void ReadRunKey(List<StartupItem> items, RegistryHive hive, string hiveLabel, string key, string? approvedSubKey)
    {
        foreach (var name in registry.GetValueNames(hive, key))
        {
            if (registry.GetValue(hive, key, name) is not string command || string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var enabled = approvedSubKey is null || IsApproved(hive, approvedSubKey, name);
            items.Add(new StartupItem(name, $@"{hiveLabel}\{key}", command, enabled));
        }
    }

    private void ReadFolder(List<StartupItem> items, string folder, RegistryHive approvedHive)
    {
        foreach (var path in files.ListFiles(folder))
        {
            var fileName = WinPath.FileName(path);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string command;
            if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = files.ReadSmallFile(path, MaxLnkSize);
                var link = bytes is null ? null : LnkParser.TryRead(bytes);
                command = link is { } l
                    ? (l.Arguments is null ? $"\"{l.Target}\"" : $"\"{l.Target}\" {l.Arguments}")
                    : $"\"{path}\"";
            }
            else
            {
                command = $"\"{path}\"";
            }

            items.Add(new StartupItem(fileName, folder, command, IsApproved(approvedHive, "StartupFolder", fileName)));
        }
    }

    private bool IsApproved(RegistryHive hive, string subKey, string name) =>
        registry.GetValue(hive, $@"{ApprovedKey}\{subKey}", name) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
}
