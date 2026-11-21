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
    public static bool OnlyAdminsCanWrite(string path, string root, bool includeRoot = false)
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

            return !includeRoot || Allowed(new DirectoryInfo(root).GetAccessControl());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Chemin long (les noms courts 8.3 comme COMMON~1 sont développés), ou le chemin tel quel s'il n'existe pas.</summary>
    public static string LongPath(string path)
    {
        var buffer = new char[1024];
        var length = GetLongPathNameW(path, buffer, (uint)buffer.Length);
        return length == 0 || length >= buffer.Length ? path : new string(buffer, 0, (int)length);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string shortPath, [Out] char[] longPath, uint size);

    /// <summary>Propriétaire Administrateurs et droits hérités seulement, sur tout l'arbre (les liens ne sont pas suivis).</summary>
    public static void ResetToInherited(string directory)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(directory));
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            var security = new DirectorySecurity();
            security.SetOwner(Administrators);
            security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
            dir.SetAccessControl(security);
            foreach (var item in dir.EnumerateFileSystemInfos("*", options))
            {
                if (item is DirectoryInfo sub)
                {
                    pending.Push(sub);
                }
                else if (item is FileInfo file)
                {
                    var fileSecurity = new FileSecurity();
                    fileSecurity.SetOwner(Administrators);
                    fileSecurity.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                    file.SetAccessControl(fileSecurity);
                }
            }
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

        // Jamais un exécutable de Windows : rundll32, cmd, mshta… lanceraient un fichier que rien ne vérifie.
        var root = Roots.FirstOrDefault(r => TrustedPaths.IsUnder(path, r));
        return root is not null && TrustedPaths.HasNoLinks(path, root)
               && TrustedPaths.OnlyAdminsCanWrite(path, root, includeRoot: root == TrustedPaths.PackageCache);
    }

    /// <summary>Tout chemin cité dans les arguments doit lui aussi être dans un dossier réservé aux administrateurs.</summary>
    public bool AreArgumentsTrusted(string arguments)
    {
        foreach (var candidate in PathsIn(arguments ?? string.Empty))
        {
            var path = TrustedPaths.Normalize(candidate);
            var root = path is null ? null : Roots.FirstOrDefault(r => TrustedPaths.IsUnder(path, r) || path.Equals(r.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (path is null || root is null || !TrustedPaths.HasNoLinks(path, root)
                || (File.Exists(path) && !TrustedPaths.OnlyAdminsCanWrite(path, root, includeRoot: root == TrustedPaths.PackageCache)))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Roots => [TrustedPaths.ProgramFiles, TrustedPaths.ProgramFilesX86, TrustedPaths.PackageCache];

    /// <summary>Chemins « X:\… » : entre guillemets, ou jusqu'au prochain « / », « - » précédé d'un espace, ou la fin.</summary>
    internal static IEnumerable<string> PathsIn(string arguments)
    {
        for (var i = 0; i + 2 < arguments.Length; i++)
        {
            if (!char.IsAsciiLetter(arguments[i]) || arguments[i + 1] != ':' || arguments[i + 2] != '\\' || (i > 0 && char.IsAsciiLetterOrDigit(arguments[i - 1])))
            {
                continue;
            }

            var quoted = i > 0 && arguments[i - 1] == '"';
            var end = quoted ? arguments.IndexOf('"', i) : NextSwitch(arguments, i);
            if (end < 0)
            {
                end = arguments.Length;
            }

            var path = arguments[i..end].Trim();
            var comma = path.IndexOf(',', StringComparison.Ordinal); // rundll32 « fichier.dll,Point d'entrée »
            yield return comma > 0 ? path[..comma] : path;
            i = end;
        }
    }

    private static int NextSwitch(string text, int start)
    {
        for (var i = start; i + 1 < text.Length; i++)
        {
            if (text[i] == ' ' && text[i + 1] is '/' or '-')
            {
                return i;
            }
        }

        return -1;
    }

    public async Task<int> RunAsync(string executable, string arguments, bool hidden, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!IsTrusted(executable) || !AreArgumentsTrusted(arguments))
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
        "Uninstall Information", "PCDoc", "Microsoft SQL Server", "Microsoft Visual Studio", "Microsoft Visual Studio 14.0", "Windows Kits",
        "Microsoft SDKs", "Microsoft.NET", "Intel", "NVIDIA Corporation", "AMD", "ATI Technologies", "Realtek", "Dolby", "Synaptics",
        "Elantech", "Conexant", "Qualcomm", "Broadcom", "Dell", "HP", "Hewlett-Packard", "Lenovo", "ASUS", "Acer", "MSI", "Logitech",
        "Google", "Mozilla Maintenance Service", "Java", "Oracle", "Package Cache", "Installer", "MSBuild",
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

        // Avant de déplacer : propriétaire Administrateurs et droits hérités partout, sinon un compte qui pouvait écrire dans le
        // dossier de l'application garderait ce droit dans la quarantaine.
        TrustedPaths.ResetToInherited(originalPath);
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
        if (TrustedPaths.Normalize(path) is not { } normalized)
        {
            return false;
        }

        var full = TrustedPaths.LongPath(normalized).TrimEnd('\\');

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
                // Suppression par handles vérifiés : un lien glissé dans l'arbre est supprimé lui-même, jamais suivi.
                CleanupFileSystem.DeleteTree(dir.FullName, Root);
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
        if (package.InstallLocation is not { Length: > 0 } raw0 || TrustedPaths.Normalize(raw0) is not { } location
            || !(TrustedPaths.IsUnder(location, Path.Combine(TrustedPaths.ProgramFiles, "WindowsApps")) || TrustedPaths.IsUnder(location, TrustedPaths.Windows)))
        {
            return null; // paquet de développement ou partage réseau : manifeste non lu en administrateur
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
