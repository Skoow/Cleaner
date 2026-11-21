using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure;

/// <summary>
/// Trouve la personne devant le PC : utilisateur de la session console active
/// (WTSQuerySessionInformation, accessible à un administrateur), puis son profil dans
/// ProfileList et ses dossiers dans HKEY_USERS\&lt;SID&gt;. Sans session interactive :
/// le compte du processus.
/// </summary>
public sealed partial class UserProfileResolver : IUserProfileResolver
{
    private const string ProfileList = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const string ShellFolders = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private const uint NoSession = 0xFFFFFFFF;

    private readonly Lazy<UserProfile> _profile;

    public UserProfileResolver() => _profile = new Lazy<UserProfile>(ResolveCore);

    public UserProfile Resolve() => _profile.Value;

    private static UserProfile ResolveCore()
    {
        using var processIdentity = WindowsIdentity.GetCurrent();
        var processSid = processIdentity.User!.Value;
        var sid = InteractiveUserSid() ?? processSid;

        using var hklm = RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var profileKey = hklm.OpenSubKey($@"{ProfileList}\{sid}");
        var profilePath = profileKey?.GetValue("ProfileImagePath") is string raw
            ? Environment.ExpandEnvironmentVariables(raw)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string Folder(string valueName, string fallbackRelative)
        {
            using var users = RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.Users, Microsoft.Win32.RegistryView.Default);
            using var key = users.OpenSubKey($@"{sid}\{ShellFolders}");
            var value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            var expanded = (value ?? $@"%USERPROFILE%\{fallbackRelative}")
                .Replace("%USERPROFILE%", profilePath, StringComparison.OrdinalIgnoreCase);
            return Environment.ExpandEnvironmentVariables(expanded);
        }

        string account;
        try
        {
            account = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            account = sid;
        }

        return new UserProfile(
            sid,
            account,
            profilePath,
            Folder("AppData", @"AppData\Roaming"),
            Folder("Local AppData", @"AppData\Local"),
            string.Equals(sid, processSid, StringComparison.OrdinalIgnoreCase));
    }

    private static string? InteractiveUserSid()
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session == NoSession)
        {
            return null;
        }

        var user = QuerySessionString(session, WtsInfoClass.WTSUserName);
        if (string.IsNullOrEmpty(user))
        {
            return null;
        }

        var domain = QuerySessionString(session, WtsInfoClass.WTSDomainName);
        try
        {
            var account = new NTAccount(string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}");
            return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (IdentityNotMappedException)
        {
            return null;
        }
    }

    private static string? QuerySessionString(uint session, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, session, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private enum WtsInfoClass
    {
        WTSUserName = 5,
        WTSDomainName = 7,
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformationW(
        IntPtr server, uint sessionId, WtsInfoClass infoClass, out IntPtr buffer, out uint bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(IntPtr memory);
}
