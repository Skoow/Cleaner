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
/// Analyse et nettoyage. Garde-fous : racines uniquement sous %SystemRoot%, %ProgramData% et le dossier local du profil
/// cible (jamais ces dossiers eux-mêmes) ; « .. », chemins réseau et chemins hors racine refusés ; seuls des fichiers
/// sont supprimés, jamais à travers un lien ; chaque chemin est revérifié juste avant sa suppression.
/// </summary>
public sealed class CleanupEngine(
    CleanupCatalog catalog, IFileSystem files, IRecycleBin recycleBin, IFileProbe environment, IUserProfileResolver profiles, IClock clock)
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
        var (bytes, items) = SafeQueryBin();
        return new CleanupReport(results, bytes, items);
    }

    /// <summary>Nettoie une catégorie ; renvoie les octets libérés.</summary>
    public long Clean(string categoryId)
    {
        if (categoryId == RecycleBinId)
        {
            var (bytes, _) = SafeQueryBin();
            recycleBin.Empty();
            return bytes;
        }

        var category = catalog.Categories.FirstOrDefault(c => c.Id == categoryId)
                       ?? throw new InvalidOperationException($"Unknown cleanup category {categoryId}");
        long freed = 0;
        foreach (var (root, entry) in EligibleWithRoot(category))
        {
            if (IsUnder(entry.Path, root) && files.TryDeleteFile(entry.Path))
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
        var limit = clock.Now.AddHours(-c.MinAgeHours);
        foreach (var root in ResolveRoots(c.Roots))
        {
            foreach (var f in files.EnumerateFiles(root, recursive: true).Where(f => c.MinAgeHours == 0 || f.LastWrite <= limit))
            {
                yield return (root, f);
            }
        }

        foreach (var file in c.Files.Select(Resolve).OfType<string>())
        {
            var parent = file[..file.LastIndexOf('\\')];
            foreach (var f in files.EnumerateFiles(parent, recursive: false).Where(f => f.Path.Equals(file, StringComparison.OrdinalIgnoreCase)))
            {
                yield return (parent, f);
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

    private string? Resolve(string raw)
    {
        var path = environment.ExpandEnvironment(raw).Replace('/', '\\').TrimEnd('\\');
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length < 4 || path[1] != ':' || path[2] != '\\'
            || path.Split('\\').Any(s => s is ".." or "." || s.Length == 0) || path.Contains('%', StringComparison.Ordinal))
        {
            return null;
        }

        return IsAllowed(path.Replace(@"\*", @"\x", StringComparison.Ordinal)) ? path : null;
    }

    private bool IsAllowed(string path)
    {
        var allowed = new[]
        {
            environment.ExpandEnvironment("%SystemRoot%"),
            environment.ExpandEnvironment("%ProgramData%"),
            profiles.Resolve().LocalAppData,
        };
        return allowed.Any(a => !string.IsNullOrEmpty(a) && !a.Contains('%', StringComparison.Ordinal) && IsUnder(path, a.TrimEnd('\\')));
    }

    private static bool IsUnder(string path, string root) =>
        path.Length > root.Length + 1 && path.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase);

    private static string Combine(string a, string b) => a.TrimEnd('\\') + @"\" + b;
}
