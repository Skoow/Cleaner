using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.Cleanup.Tests;

/// <summary>Faux disque en mémoire (chemins Windows). Les dossiers « liens » ne sont pas traversés, comme le vrai.</summary>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deleted { get; } = [];

    public List<string> EmptiedRoots { get; } = [];

    public FakeFileSystem File(string path, long size, DateTimeOffset lastWrite, DateTimeOffset? created = null)
    {
        _files[path] = new FileEntry(path, size, lastWrite, created ?? lastWrite);
        for (var dir = Parent(path); dir is not null; dir = Parent(dir))
        {
            _dirs.Add(dir);
        }

        return this;
    }

    public bool DirectoryExists(string path) => _dirs.Contains(path.TrimEnd('\\'));

    public IReadOnlyList<string> ListDirectories(string path)
    {
        var prefix = path.TrimEnd('\\') + @"\";
        return [.. _dirs.Where(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !d[prefix.Length..].Contains('\\', StringComparison.Ordinal)
                                    && !Links.Contains(d))];
    }

    public IEnumerable<FileEntry> EnumerateFiles(string root, bool recursive)
    {
        var prefix = root.TrimEnd('\\') + @"\";
        return _files.Values.Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                        && (recursive || !f.Path[prefix.Length..].Contains('\\', StringComparison.Ordinal))
                                        && !Links.Any(l => f.Path.StartsWith(l + @"\", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public bool TryDeleteFile(string path, string allowedRoot)
    {
        if (!path.StartsWith(allowedRoot.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Engine passed a path outside its root");
        }

        if (Locked.Contains(path) || !_files.Remove(path))
        {
            return false;
        }

        Deleted.Add(path);
        return true;
    }

    public void DeleteEmptySubdirectories(string root) => EmptiedRoots.Add(root);

    private static string? Parent(string path)
    {
        var cut = path.LastIndexOf('\\');
        return cut <= 2 ? null : path[..cut];
    }
}

public sealed class FakeRecycleBin(long bytes, long items) : IRecycleBin
{
    public bool Emptied { get; private set; }

    public (long Bytes, long Items) Query() => Emptied ? (0, 0) : (bytes, items);

    public void Empty() => Emptied = true;
}
