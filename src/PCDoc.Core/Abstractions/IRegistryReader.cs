namespace PCDoc.Core.Abstractions;

/// <summary>Ruche lue. <see cref="TargetUser"/> = HKEY_USERS\&lt;SID du profil cible&gt;, jamais le HKCU du processus.</summary>
public enum RegistryHive
{
    LocalMachine,
    TargetUser,
}

/// <summary>Vue du registre (64 bits, 32 bits ou celle du processus).</summary>
public enum RegistryView
{
    Default,
    Registry64,
    Registry32,
}

/// <summary>Lecture du registre. Une clé absente ou refusée donne une liste vide ou <c>null</c>.</summary>
public interface IRegistryReader
{
    IReadOnlyList<string> GetValueNames(RegistryHive hive, string path, RegistryView view = RegistryView.Default);

    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string path, RegistryView view = RegistryView.Default);

    object? GetValue(RegistryHive hive, string path, string name, RegistryView view = RegistryView.Default);
}
