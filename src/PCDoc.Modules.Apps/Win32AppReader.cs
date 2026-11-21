using System.Globalization;
using System.Text.RegularExpressions;
using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.Apps;

/// <summary>
/// Programmes Win32 des clés Uninstall (HKLM 64 et 32 bits, profil cible). Composants système, mises à jour et
/// entrées sans nom sont ignorés.
/// </summary>
public sealed partial class Win32AppReader(IRegistryReader registry)
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (RegistryHive Hive, RegistryView View)[] Sources =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.TargetUser, RegistryView.Default),
    ];

    public IReadOnlyList<InstalledApp> Read()
    {
        var apps = new List<InstalledApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in Sources)
        {
            foreach (var key in registry.GetSubKeyNames(hive, UninstallPath, view))
            {
                if (ReadOne(hive, view, key) is { } app && seen.Add($"{app.Scope}|{key}|{app.Name}"))
                {
                    apps.Add(app);
                }
            }
        }

        return apps;
    }

    /// <summary>Vrai si l'entrée existe toujours (le programme n'a pas été désinstallé).</summary>
    public bool StillInstalled(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.RegistryPath is { } path
               && registry.GetValue(app.Scope == AppScope.User ? RegistryHive.TargetUser : RegistryHive.LocalMachine, path, "DisplayName", app.View) is string;
    }

    private InstalledApp? ReadOne(RegistryHive hive, RegistryView view, string key)
    {
        var path = $@"{UninstallPath}\{key}";
        string? Text(string name) => registry.GetValue(hive, path, name, view) is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        int? Number(string name) => registry.GetValue(hive, path, name, view) is int i ? i : null;

        var name = Text("DisplayName");
        if (name is null || Number("SystemComponent") == 1 || Text("ParentKeyName") is not null
            || Text("ReleaseType") is { } release && (release.Contains("Update", StringComparison.OrdinalIgnoreCase) || release.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var uninstall = Text("UninstallString");
        var isMsi = Number("WindowsInstaller") == 1 || (uninstall?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) ?? false);
        string? productCode = null;
        if (isMsi)
        {
            productCode = Guid().Match(key) is { Success: true } k && k.Length == key.Length ? key.ToUpperInvariant()
                : uninstall is not null && Guid().Match(uninstall) is { Success: true } m ? m.Value.ToUpperInvariant() : null;
        }

        var scope = hive == RegistryHive.TargetUser ? AppScope.User : AppScope.Machine;
        var viewTag = view switch { RegistryView.Registry32 => "32", RegistryView.Registry64 => "64", _ => "u" };
        return new InstalledApp(
            $"win32:{viewTag}:{(scope == AppScope.User ? "user" : "machine")}:{key}",
            AppKind.Win32,
            scope,
            name,
            Text("Publisher"),
            Text("DisplayVersion"),
            Number("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : 0,
            Text("InstallDate") is { } d && DateOnly.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            uninstall,
            Text("QuietUninstallString"),
            Text("InstallLocation")?.TrimEnd('\\'),
            path,
            view,
            productCode);
    }

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    internal static partial Regex Guid();
}
