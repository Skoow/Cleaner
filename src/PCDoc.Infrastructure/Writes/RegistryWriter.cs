using Microsoft.Win32;
using PCDoc.Core.Abstractions;
using CoreHive = PCDoc.Core.Abstractions.RegistryHive;

namespace PCDoc.Infrastructure.Writes;

/// <summary>Écritures registre des correcteurs ; « TargetUser » = HKEY_USERS\&lt;SID du profil cible&gt;.</summary>
public sealed class RegistryWriter(IUserProfileResolver profiles) : IRegistryWriter
{
    public void SetBinary(CoreHive hive, string path, string name, byte[] value)
    {
        using var key = Root(hive).CreateSubKey(Full(hive, path), writable: true);
        key.SetValue(name, value, RegistryValueKind.Binary);
    }

    public void DeleteValue(CoreHive hive, string path, string name)
    {
        using var key = Root(hive).OpenSubKey(Full(hive, path), writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public string FullPath(CoreHive hive, string path) =>
        (hive == CoreHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_USERS") + @"\" + Full(hive, path);

    private static RegistryKey Root(CoreHive hive) => RegistryKey.OpenBaseKey(
        hive == CoreHive.LocalMachine ? Microsoft.Win32.RegistryHive.LocalMachine : Microsoft.Win32.RegistryHive.Users,
        Microsoft.Win32.RegistryView.Registry64);

    private string Full(CoreHive hive, string path) =>
        hive == CoreHive.LocalMachine ? path : $@"{profiles.Resolve().Sid}\{path}";
}
