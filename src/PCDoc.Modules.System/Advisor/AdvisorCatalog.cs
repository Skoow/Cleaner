using System.Text.Json;
using System.Text.Json.Serialization;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.System.Advisor;

/// <summary>Catalogue du conseiller (Definitions/advisor.json).</summary>
public sealed class AdvisorCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private AdvisorCatalog(CatalogFile file)
    {
        Version = file.Version;
        Services = (file.Services ?? []).ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        LockedServices = new HashSet<string>(file.LockedServices ?? [], StringComparer.OrdinalIgnoreCase);
        Tasks = (file.Tasks ?? []).ToDictionary(t => t.Path!, StringComparer.OrdinalIgnoreCase);
        LockedTaskPrefixes = file.LockedTaskPrefixes ?? [];
        Startup = file.Startup ?? [];
    }

    public static AdvisorCatalog Default { get; } = LoadEmbedded();

    public int Version { get; }

    public IReadOnlyDictionary<string, CatalogItem> Services { get; }

    public IReadOnlySet<string> LockedServices { get; }

    public IReadOnlyDictionary<string, CatalogItem> Tasks { get; }

    public IReadOnlyList<string> LockedTaskPrefixes { get; }

    public IReadOnlyList<CatalogItem> Startup { get; }

    public static AdvisorCatalog Load(string json) =>
        new(JsonSerializer.Deserialize<CatalogFile>(json, Options) ?? throw new JsonException("Empty advisor catalog"));

    private static AdvisorCatalog LoadEmbedded()
    {
        using var stream = typeof(AdvisorCatalog).Assembly.GetManifestResourceStream("PCDoc.Definitions.advisor.json")
                           ?? throw new InvalidOperationException("Embedded advisor catalog missing");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

#pragma warning disable CA1812 // Instancié par le désérialiseur.
    private sealed record CatalogFile(
        int Version,
        CatalogItem[]? Services,
        string[]? LockedServices,
        CatalogItem[]? Tasks,
        string[]? LockedTaskPrefixes,
        CatalogItem[]? Startup);
#pragma warning restore CA1812
}

/// <summary>Une entrée du catalogue : niveau, condition éventuelle, clé du texte d'explication.</summary>
public sealed record CatalogItem(string Name, string? Path, RiskLevel Level, string? Condition, string Reason, bool Prefix = false);
