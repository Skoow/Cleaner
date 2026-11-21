using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>Chemins réservés aux administrateurs : aucun lien dans le chemin, aucun droit d'écriture pour un autre compte.</summary>
internal static class TrustedPaths
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    // Écrire, ajouter, supprimer, changer les droits ou le propriétaire (y compris les droits génériques).
    private const int WriteMask = 0x2 | 0x4 | 0x40 | 0x10000 | 0x40000 | 0x80000 | 0x10000000 | 0x40000000;

    public static string ProgramFiles { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static string ProgramFilesX86 { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    public static string Windows { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static string PackageCache { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Package Cache");

    /// <summary>Le chemin complet normalisé, ou null s'il est relatif, réseau ou contient « .. ».</summary>
    public static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        return Path.GetFullPath(path).TrimEnd('\\');
    }

    public static bool IsUnder(string path, string root) =>
        root.Length > 0 && path.StartsWith(root.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Aucun élément du chemin, de <paramref name="root"/> exclu jusqu'à <paramref name="path"/>, n'est un lien.</summary>
    public static bool HasNoLinks(string path, string root)
    {
        for (var current = path; IsUnder(current, root); current = Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Le fichier et ses dossiers jusqu'à <paramref name="root"/> (exclu) ne sont modifiables que par les administrateurs.</summary>
    public static bool OnlyAdminsCanWrite(string path, string root)
    {
        try
        {
            if (!Allowed(new FileInfo(path).GetAccessControl()))
            {
                return false;
            }

            for (var dir = Path.GetDirectoryName(path); dir is not null && IsUnder(dir, root); dir = Path.GetDirectoryName(dir))
            {
                if (!Allowed(new DirectoryInfo(dir).GetAccessControl()))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsAdmin(IdentityReference? id) => id is SecurityIdentifier s && (s == Administrators || s == LocalSystem || s == TrustedInstaller);

    private static bool Allowed(FileSystemSecurity security)
    {
        if (!IsAdmin(security.GetOwner(typeof(SecurityIdentifier))))
        {
            return false;
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow && !rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)
                && ((int)rule.FileSystemRights & WriteMask) != 0 && !IsAdmin(rule.IdentityReference))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Lance le désinstalleur d'un éditeur, seulement s'il est dans un dossier réservé aux administrateurs.</summary>
public sealed class UninstallLauncher : IUninstallLauncher
{
    public bool IsTrusted(string executable)
    {
        if (TrustedPaths.Normalize(executable) is not { } path || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        var root = new[] { TrustedPaths.ProgramFiles, TrustedPaths.ProgramFilesX86, TrustedPaths.Windows, TrustedPaths.PackageCache }
            .FirstOrDefault(r => TrustedPaths.IsUnder(path, r));
        return root is not null && TrustedPaths.HasNoLinks(path, root) && TrustedPaths.OnlyAdminsCanWrite(path, root);
    }

    public async Task<int> RunAsync(string executable, string arguments, bool hidden, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!IsTrusted(executable))
        {
            throw new InvalidOperationException("Uninstaller is not in a protected folder");
        }

        var path = TrustedPaths.Normalize(executable)!;
        var start = new ProcessStartInfo(path, arguments ?? string.Empty)
        {
            UseShellExecute = false,
            CreateNoWindow = hidden,
            WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Uninstaller did not start");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Déjà terminé.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"Uninstaller did not finish within {timeout.TotalMinutes:0} minutes");
        }

        return process.ExitCode;
    }
}

/// <summary>
/// Quarantaine des restes dans %ProgramData%\PCDoc\quarantine (protégé). Chaque dossier est nommé
/// « aaaaMMjj-HHmmss-empreinte-nom » ; la date du nom sert à la purge.
/// </summary>
public sealed class Quarantine(IClock clock) : IQuarantine
{
    private static readonly HashSet<string> SharedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common Files", "WindowsApps", "ModifiableWindowsApps", "Windows Defender", "Windows Defender Advanced Threat Protection", "Windows NT",
        "Windows Mail", "Windows Media Player", "Windows Photo Viewer", "Windows Sidebar", "WindowsPowerShell", "Windows Security",
        "Microsoft", "Microsoft Office", "Microsoft Update Health Tools", "Internet Explorer", "Reference Assemblies", "MSBuild", "dotnet",
        "Uninstall Information", "PCDoc",
    };

    private static string Root => SecureFolders.Ensure(Path.Combine(SecureFolders.Root, "quarantine"));

    public string PlanFor(string originalPath)
    {
        var name = new string([.. Path.GetFileName(originalPath.TrimEnd('\\')).Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')]);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalPath.ToUpperInvariant())))[..8];
        var stamp = clock.Now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(Root, $"{stamp}-{hash}-{(name.Length == 0 ? "folder" : name[..Math.Min(name.Length, 60)])}");
    }

    public void Move(string originalPath, string quarantinePath)
    {
        if (!IsLeftoverCandidate(originalPath) || !InQuarantine(quarantinePath) || Directory.Exists(quarantinePath))
        {
            throw new InvalidOperationException("Quarantine refused");
        }

        Directory.Move(originalPath, quarantinePath);
    }

    public void Restore(string quarantinePath, string originalPath)
    {
        if (!InQuarantine(quarantinePath) || !Directory.Exists(quarantinePath) || !IsLeftoverCandidate(originalPath) || Directory.Exists(originalPath)
            || !Directory.Exists(Path.GetDirectoryName(originalPath)))
        {
            throw new InvalidOperationException("Restore refused: the folder exists again or the quarantine copy is gone");
        }

        Directory.Move(quarantinePath, originalPath);
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsLeftoverCandidate(string path)
    {
        if (TrustedPaths.Normalize(path) is not { } full)
        {
            return false;
        }

        foreach (var root in new[] { TrustedPaths.ProgramFiles, TrustedPaths.ProgramFilesX86 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TrustedPaths.IsUnder(full, root))
            {
                var top = full[(root.TrimEnd('\\').Length + 1)..].Split('\\')[0];
                return !SharedFolders.Contains(top) && TrustedPaths.HasNoLinks(full, root);
            }
        }

        return false;
    }

    public void PurgeOlderThan(TimeSpan age)
    {
        var limit = clock.Now.UtcDateTime - age;
        foreach (var dir in new DirectoryInfo(Root).EnumerateDirectories())
        {
            if (dir.Name.Length >= 15 && DateTime.TryParseExact(dir.Name[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var stamp) && stamp < limit)
            {
                try
                {
                    // Directory.Delete ne suit pas les liens : il supprime le lien, jamais sa cible.
                    dir.Delete(recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Fichier en cours d'utilisation : nouvel essai au prochain démarrage.
                }
            }
        }
    }

    private static bool InQuarantine(string path) =>
        TrustedPaths.Normalize(path) is { } full && Path.GetDirectoryName(full) is { } parent
        && parent.Equals(Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && !File.Exists(full);
}

/// <summary>Nom affiché d'une application du Store, lu dans son AppxManifest.xml (ressources « ms-resource: » résolues par le Shell).</summary>
public sealed partial class AppDisplayNames : IAppDisplayNames
{
    public string? Resolve(StorePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.InstallLocation is not { Length: > 0 } location)
        {
            return null;
        }

        var raw = ReadDisplayName(Path.Combine(location, "AppxManifest.xml"));
        if (raw is null || !raw.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }

        var key = raw["ms-resource:".Length..];
        var candidates = key.StartsWith("//", StringComparison.Ordinal) ? [$"ms-resource:{key}"]
            : key.Contains('/', StringComparison.Ordinal) ? [$"ms-resource://{package.Name}/{key.TrimStart('/')}"]
            : new[] { $"ms-resource://{package.Name}/resources/{key}", $"ms-resource://{package.Name}/{key}" };
        foreach (var candidate in candidates)
        {
            if (Load($"@{{{package.FullName}?{candidate}}}") is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static string? ReadDisplayName(string manifest)
    {
        try
        {
            using var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var inProperties = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Properties")
                {
                    inProperties = true;
                }
                else if (inProperties && reader.NodeType == XmlNodeType.Element && reader.LocalName == "DisplayName")
                {
                    return reader.ReadElementContentAsString();
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Properties")
                {
                    return null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            // Manifeste illisible : le nom du paquet sera affiché.
        }

        return null;
    }

    private static unsafe string? Load(string source)
    {
        var buffer = new char[512];
        fixed (char* p = buffer)
        {
            if (SHLoadIndirectString(source, p, (uint)buffer.Length, IntPtr.Zero) != 0)
            {
                return null;
            }
        }

        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHLoadIndirectString(string source, char* output, uint size, IntPtr reserved);
}
