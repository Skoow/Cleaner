using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Cleanup;

public sealed record CategoryResult(string Id, RiskLevel Risk, long Bytes, int Files);

public sealed record CleanupReport(IReadOnlyList<CategoryResult> Categories, long RecycleBinBytes, long RecycleBinItems);

/// <summary>Source du nettoyage (simulable dans les tests de la page).</summary>
public interface ICleanupSource
{
    CleanupReport Analyze();

    IReadOnlyList<FileEntry> BigFiles();
}

/// <summary>
/// Analyse et nettoyage. Garde-fous : racines uniquement sous le dossier Windows, ProgramData (dossiers connus, jamais les
/// variables d'environnement) et &lt;profil&gt;\AppData\Local du profil cible (jamais la redirection « Local AppData »,
/// modifiable par l'utilisateur) ; jamais ces dossiers eux-mêmes ; « .. », chemins réseau et variables inconnues refusés ;
/// seuls des fichiers sont supprimés, et le système vérifie leur chemin réel juste avant (liens, liens physiques).
/// </summary>
public sealed class CleanupEngine(
    CleanupCatalog catalog, IFileSystem files, IRecycleBin recycleBin, IKnownFolders known, IUserProfileResolver profiles, IClock clock,
    ICleanupGuards guards)
    : ICleanupSource
{
    public const string RecycleBinId = "recycle-bin";
    public const string ComponentStoreId = "component-store";
    private const long BigFileThreshold = 500L * 1024 * 1024;
    private static readonly string[] UserFolders = ["Downloads", "Desktop", "Documents", "Videos"];

    public CleanupReport Analyze()
    {
        var results = catalog.Categories.Select(c =>
        {
            var eligible = Eligible(c).ToList();
            return new CategoryResult(c.Id, c.Risk, eligible.Sum(f => f.Size), eligible.Count);
        }).ToList();
        var (bytes, items) = RecycleBinAllowed ? SafeQueryBin() : (0, 0);
        return new CleanupReport(results, bytes, items);
    }

    /// <summary>La corbeille de l'API est celle du compte qui exécute PCDoc : proposée seulement si c'est le profil cible.</summary>
    private bool RecycleBinAllowed => profiles.Resolve().IsProcessUser;

    /// <summary>Nettoie une catégorie ; renvoie les octets libérés.</summary>
    public long Clean(string categoryId)
    {
        if (categoryId == RecycleBinId)
        {
            if (!RecycleBinAllowed)
            {
                throw new InvalidOperationException("The Recycle Bin of another account cannot be emptied");
            }

            var (bytes, _) = SafeQueryBin();
            recycleBin.Empty();
            return bytes;
        }

        var category = catalog.Categories.FirstOrDefault(c => c.Id == categoryId)
                       ?? throw new InvalidOperationException($"Unknown cleanup category {categoryId}");
        long freed = 0;
        foreach (var (root, entry) in EligibleWithRoot(category))
        {
            if (IsUnder(entry.Path, root) && files.TryDeleteFile(entry.Path, root))
            {
                freed += entry.Size;
            }
        }

        foreach (var root in ResolveRoots(category.Roots))
        {
            files.DeleteEmptySubdirectories(root);
        }

        return freed;
    }

    public IReadOnlyList<FileEntry> BigFiles()
    {
        var profile = profiles.Resolve().ProfilePath;
        return [.. UserFolders
            .Select(f => Combine(profile, f))
            .Where(files.DirectoryExists)
            .SelectMany(f => files.EnumerateFiles(f, recursive: true))
            .Where(f => f.Size >= BigFileThreshold)
            .OrderByDescending(f => f.Size)
            .Take(30)];
    }

    public bool IsKnown(string categoryId) =>
        categoryId is RecycleBinId or ComponentStoreId || catalog.Categories.Any(c => c.Id == categoryId);

    public RiskLevel RiskOf(string categoryId) => categoryId switch
    {
        RecycleBinId => RiskLevel.Conditional,
        ComponentStoreId => RiskLevel.Safe,
        _ => catalog.Categories.FirstOrDefault(c => c.Id == categoryId)?.Risk ?? RiskLevel.Locked,
    };

    private (long, long) SafeQueryBin()
    {
        try
        {
            return recycleBin.Query();
        }
        catch (InvalidOperationException)
        {
            return (0, 0);
        }
    }

    private IEnumerable<FileEntry> Eligible(CleanupCategory c) => EligibleWithRoot(c).Select(x => x.Entry);

    private IEnumerable<(string Root, FileEntry Entry)> EligibleWithRoot(CleanupCategory c)
    {
        if (c.RequiresIdleWindowsUpdate && guards.IsWindowsUpdateBusy())
        {
            yield break; // une mise à jour est en cours : ses fichiers ne sont pas touchés
        }

        if (c.RequiresClosed?.Any(guards.IsRunning) == true)
        {
            yield break; // navigateur ouvert : ses cookies et son historique sont en cours d'utilisation
        }

        var limit = clock.Now.AddHours(-c.MinAgeHours);
        foreach (var root in ResolveRoots(c.Roots))
        {
            var all = files.EnumerateFiles(root, recursive: true).ToList();
            if (c.MinAgeHours == 0)
            {
                foreach (var f in all)
                {
                    yield return (root, f);
                }

                continue;
            }

            // Un sous-dossier qui contient un fichier récent (installation en cours, archive juste extraite) est gardé entier.
            var busy = all.Where(f => f.LastTouched > limit).Select(f => TopFolder(root, f.Path)).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in all.Where(f => f.LastTouched <= limit && (TopFolder(root, f.Path) is not { } top || !busy.Contains(top))))
            {
                yield return (root, f);
            }
        }

        // Fichiers précis : « * » permis dans les dossiers (profils) et dans le nom (thumbcache_*.db, *.lnk).
        foreach (var file in c.Files.Select(Resolve).OfType<string>())
        {
            var cut = file.LastIndexOf('\\');
            var pattern = file[(cut + 1)..];
            var folder = file[..cut];
            // Le fichier lui-même a été vérifié par Resolve ; son dossier peut être une racine autorisée (C:\Windows\MEMORY.DMP).
            IEnumerable<string> parents = folder.Contains('*', StringComparison.Ordinal) ? ResolveRoots([folder]) : files.DirectoryExists(folder) ? [folder] : [];
            foreach (var parent in parents)
            {
                foreach (var f in files.EnumerateFiles(parent, recursive: false).Where(f => NameMatches(f.Path[(f.Path.LastIndexOf('\\') + 1)..], pattern)))
                {
                    yield return (parent, f);
                }
            }
        }
    }

    /// <summary>Développe les variables et les segments « * » ; ne garde que les dossiers existants et autorisés.</summary>
    private IEnumerable<string> ResolveRoots(IEnumerable<string> roots)
    {
        foreach (var raw in roots)
        {
            if (Resolve(raw) is not { } path)
            {
                continue;
            }

            IEnumerable<string> candidates = [path.Split('\\')[0]];
            foreach (var segment in path.Split('\\').Skip(1))
            {
                candidates = segment == "*"
                    ? candidates.SelectMany(files.ListDirectories)
                    : candidates.Select(c => c + @"\" + segment);
            }

            foreach (var dir in candidates.Where(d => IsAllowed(d) && files.DirectoryExists(d)))
            {
                yield return dir;
            }
        }
    }

    internal static bool NameMatches(string name, string pattern)
    {
        var star = pattern.IndexOf('*', StringComparison.Ordinal);
        return star < 0
            ? name.Equals(pattern, StringComparison.OrdinalIgnoreCase)
            : name.Length >= pattern.Length - 1 && name.StartsWith(pattern[..star], StringComparison.OrdinalIgnoreCase)
              && name.EndsWith(pattern[(star + 1)..], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Premier sous-dossier sous la racine (null pour un fichier posé directement dans la racine).</summary>
    private static string? TopFolder(string root, string path)
    {
        var rest = path[(root.Length + 1)..];
        var cut = rest.IndexOf('\\', StringComparison.Ordinal);
        return cut < 0 ? null : rest[..cut];
    }

    private string LocalAppData => profiles.Resolve().ProfilePath.TrimEnd('\\') + @"\AppData\Local";

    private string RoamingAppData => profiles.Resolve().ProfilePath.TrimEnd('\\') + @"\AppData\Roaming";

    /// <summary>Seules trois variables sont connues, toutes issues de sources de confiance.</summary>
    private string Expand(string raw) => raw
        .Replace("%SystemRoot%", known.Windows, StringComparison.OrdinalIgnoreCase)
        .Replace("%windir%", known.Windows, StringComparison.OrdinalIgnoreCase)
        .Replace("%ProgramData%", known.ProgramData, StringComparison.OrdinalIgnoreCase)
        .Replace("%LOCALAPPDATA%", LocalAppData, StringComparison.OrdinalIgnoreCase)
        .Replace("%APPDATA%", RoamingAppData, StringComparison.OrdinalIgnoreCase);

    private string? Resolve(string raw)
    {
        var path = Expand(raw).Replace('/', '\\').TrimEnd('\\');
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length < 4 || path[1] != ':' || path[2] != '\\'
            || path.Split('\\').Any(s => s is ".." or "." || s.Length == 0) || path.Contains('%', StringComparison.Ordinal))
        {
            return null;
        }

        return IsAllowed(path.Replace("*", "x", StringComparison.Ordinal)) ? path : null;
    }

    private bool IsAllowed(string path)
    {
        var allowed = new[] { known.Windows, known.ProgramData, LocalAppData, RoamingAppData };
        return allowed.Any(a => !string.IsNullOrEmpty(a) && !a.Contains('%', StringComparison.Ordinal) && IsUnder(path, a.TrimEnd('\\')));
    }

    private static bool IsUnder(string path, string root) =>
        path.Length > root.Length + 1 && path.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase);

    private static string Combine(string a, string b) => a.TrimEnd('\\') + @"\" + b;
}
