using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCDoc.Modules.System.Inventory;

/// <summary>
/// Processeurs compatibles Windows 11 : expressions régulières « supportés » puis « non supportés » ;
/// aucun motif → inconnu (pas d'avertissement). Fichier : Definitions/win11-supported-cpus.json.
/// </summary>
public sealed class Win11CpuList
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private readonly Regex[] _supported;
    private readonly Regex[] _unsupported;

    private Win11CpuList(int version, IEnumerable<string> supported, IEnumerable<string> unsupported)
    {
        Version = version;
        _supported = [.. supported.Select(Compile)];
        _unsupported = [.. unsupported.Select(Compile)];
    }

    public static Win11CpuList Default { get; } = LoadEmbedded();

    public int Version { get; }

    public int SupportedCount => _supported.Length;

    public int UnsupportedCount => _unsupported.Length;

    public static Win11CpuList Load(string json)
    {
        var file = JsonSerializer.Deserialize<CpuFile>(json)
                   ?? throw new JsonException("Empty CPU list");
        return new Win11CpuList(file.version, file.supported ?? [], file.unsupported ?? []);
    }

    public CheckState Check(string? cpuName)
    {
        if (string.IsNullOrWhiteSpace(cpuName))
        {
            return CheckState.Unknown;
        }

        var name = Regex.Replace(cpuName.Trim(), @"\s+", " ", RegexOptions.None, RegexTimeout);
        if (_supported.Any(r => r.IsMatch(name)))
        {
            return CheckState.Pass;
        }

        return _unsupported.Any(r => r.IsMatch(name)) ? CheckState.Fail : CheckState.Unknown;
    }

    private static Regex Compile(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static Win11CpuList LoadEmbedded()
    {
        using var stream = typeof(Win11CpuList).Assembly.GetManifestResourceStream("PCDoc.Definitions.win11-supported-cpus.json")
                           ?? throw new InvalidOperationException("Embedded CPU list missing");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

#pragma warning disable IDE1006, CA1812 // Noms du fichier JSON ; instancié par le désérialiseur.
    private sealed record CpuFile(int version, string[]? supported, string[]? unsupported);
#pragma warning restore IDE1006, CA1812
}
