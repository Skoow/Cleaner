using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Tests.Fakes;

/// <summary>Faux système de fichiers : fichiers existants, variables, contenu de petits fichiers.</summary>
public sealed class FakeFileProbe : IFileProbe
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _contents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _env = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = @"C:\Windows",
        ["windir"] = @"C:\Windows",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramData"] = @"C:\ProgramData",
        ["APPDATA"] = @"C:\Users\Lea\AppData\Roaming",
    };

    public FakeFileProbe WithFiles(params string[] paths)
    {
        foreach (var p in paths)
        {
            _files.Add(p);
        }

        return this;
    }

    public FakeFileProbe WithContent(string path, byte[] content)
    {
        _files.Add(path);
        _contents[path] = content;
        return this;
    }

    public bool Exists(string path) => _files.Contains(path);

    public string ExpandEnvironment(string value)
    {
        foreach (var (k, v) in _env)
        {
            value = value.Replace($"%{k}%", v, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

    public IReadOnlyList<string> ListFiles(string directory)
    {
        var prefix = directory.TrimEnd('\\') + @"\";
        return [.. _files.Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !f[prefix.Length..].Contains('\\', StringComparison.Ordinal))];
    }

    public byte[]? ReadSmallFile(string path, int maxBytes) =>
        _contents.TryGetValue(path, out var c) && c.Length <= maxBytes ? c : null;
}
