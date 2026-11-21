using System.Security;
using Microsoft.Win32;
using PCDoc.Core.Abstractions;
using CoreHive = PCDoc.Core.Abstractions.RegistryHive;
using CoreView = PCDoc.Core.Abstractions.RegistryView;

namespace PCDoc.Infrastructure;

/// <summary>
/// Lecture du registre. <see cref="CoreHive.TargetUser"/> ouvre HKEY_USERS\&lt;SID du profil cible&gt;
/// (jamais Registry.CurrentUser, qui serait le compte administrateur qui a lancé PCDoc).
/// </summary>
public sealed class RegistryReader(IUserProfileResolver profiles) : IRegistryReader
{
    private readonly Lazy<string> _targetSid = new(() => profiles.Resolve().Sid);

    public IReadOnlyList<string> GetValueNames(CoreHive hive, string path, CoreView view = CoreView.Default) =>
        WithKey(hive, path, view, key => (IReadOnlyList<string>)key.GetValueNames(), []);

    public IReadOnlyList<string> GetSubKeyNames(CoreHive hive, string path, CoreView view = CoreView.Default) =>
        WithKey(hive, path, view, key => (IReadOnlyList<string>)key.GetSubKeyNames(), []);

    public object? GetValue(CoreHive hive, string path, string name, CoreView view = CoreView.Default) =>
        WithKey(hive, path, view, key => key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), null);

    private T WithKey<T>(CoreHive hive, string path, CoreView view, Func<RegistryKey, T> read, T fallback)
    {
        try
        {
            var (baseHive, prefix) = hive == CoreHive.LocalMachine
                ? (Microsoft.Win32.RegistryHive.LocalMachine, string.Empty)
                : (Microsoft.Win32.RegistryHive.Users, _targetSid.Value);
            var fullPath = string.IsNullOrEmpty(prefix) ? path : string.IsNullOrEmpty(path) ? prefix : $@"{prefix}\{path}";
            using var root = RegistryKey.OpenBaseKey(baseHive, Map(view));
            using var key = string.IsNullOrEmpty(fullPath) ? root : root.OpenSubKey(fullPath, writable: false);
            return key is null ? fallback : read(key);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return fallback;
        }
    }

    private static Microsoft.Win32.RegistryView Map(CoreView view) => view switch
    {
        CoreView.Registry64 => Microsoft.Win32.RegistryView.Registry64,
        CoreView.Registry32 => Microsoft.Win32.RegistryView.Registry32,
        _ => Microsoft.Win32.RegistryView.Default,
    };
}
