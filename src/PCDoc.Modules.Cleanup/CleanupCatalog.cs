using System.Text.Json;
using System.Text.Json.Serialization;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Cleanup;

/// <summary>Une catégorie de nettoyage (Definitions/cleanup.json).</summary>
public sealed record CleanupCategory(
    string Id, IReadOnlyList<string> Roots, IReadOnlyList<string> Files, int MinAgeHours, RiskLevel Risk, bool RequiresIdleWindowsUpdate = false);

/// <summary>Catalogue des emplacements nettoyables.</summary>
public sealed class CleanupCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private CleanupCatalog(int version, IReadOnlyList<CleanupCategory> categories)
    {
        Version = version;
        Categories = categories;
    }

    public static CleanupCatalog Default { get; } = LoadEmbedded();

    public int Version { get; }

    public IReadOnlyList<CleanupCategory> Categories { get; }

    public static CleanupCatalog Load(string json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, Options) ?? throw new JsonException("Empty cleanup catalog");
        return new CleanupCatalog(file.Version, [.. (file.Categories ?? [])
            .Where(c => c.Risk is RiskLevel.Safe or RiskLevel.Conditional) // jamais de catégorie 🔴/🔒 dans un nettoyage
            .Select(c => new CleanupCategory(c.Id, c.Roots ?? [], c.Files ?? [], Math.Max(0, c.MinAgeHours), c.Risk, c.RequiresIdleWindowsUpdate))]);
    }

    private static CleanupCatalog LoadEmbedded()
    {
        using var stream = typeof(CleanupCatalog).Assembly.GetManifestResourceStream("PCDoc.Definitions.cleanup.json")
                           ?? throw new InvalidOperationException("Embedded cleanup catalog missing");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

#pragma warning disable CA1812
    private sealed record CatalogFile(int Version, Item[]? Categories);

    private sealed record Item(string Id, string[]? Roots, string[]? Files, int MinAgeHours, RiskLevel Risk, bool RequiresIdleWindowsUpdate);
#pragma warning restore CA1812
}
