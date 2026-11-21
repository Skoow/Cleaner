using System.Text.Json;
using System.Text.Json.Serialization;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Optimize;

public enum TweakKind
{
    Registry,
    PowerPlan,
    Hibernate,
}

/// <summary>Valeur cible : int (DWORD) ou string (REG_SZ).</summary>
public sealed record TweakValue(RegistryHive Hive, string Path, string Name, object Target);

/// <summary>Un réglage réversible du catalogue Definitions/optimize.json.</summary>
public sealed record Tweak(
    string Id,
    TweakKind Kind,
    RiskLevel Risk,
    RiskLevel? DesktopRisk,
    bool InBilan,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<TweakValue> Values,
    string? Plan,
    bool AbsentIsApplied = false);

public sealed class TweakCatalog
{
    public static readonly IReadOnlyList<string> ProfileIds = ["gaming", "office", "battery"];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private TweakCatalog(IReadOnlyList<Tweak> tweaks) => Tweaks = tweaks;

    public static TweakCatalog Default { get; } = LoadEmbedded();

    public IReadOnlyList<Tweak> Tweaks { get; }

    public Tweak? Find(string id) => Tweaks.FirstOrDefault(t => t.Id == id);

    public static TweakCatalog Load(string json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, Options) ?? throw new JsonException("Empty optimize catalog");
        var tweaks = new List<Tweak>();
        foreach (var t in file.Tweaks ?? [])
        {
            var values = (t.Values ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v.Path) && !string.IsNullOrWhiteSpace(v.Name) && (v.Dword is not null) != (v.Text is not null))
                .Select(v => new TweakValue(v.Hive, v.Path!, v.Name!, v.Dword is { } d ? d : v.Text!))
                .ToList();
            var valid = t.Kind switch
            {
                TweakKind.Registry => values.Count > 0,
                TweakKind.PowerPlan => Guid.TryParse(t.Plan, out _),
                _ => true,
            };
            if (valid && !string.IsNullOrWhiteSpace(t.Id) && t.Risk is not RiskLevel.Locked)
            {
                tweaks.Add(new Tweak(t.Id, t.Kind, t.Risk, t.DesktopRisk, t.Bilan, t.Profiles ?? [], values, t.Plan?.ToLowerInvariant(), t.AbsentIsApplied));
            }
        }

        return new TweakCatalog(tweaks);
    }

    private static TweakCatalog LoadEmbedded()
    {
        using var stream = typeof(TweakCatalog).Assembly.GetManifestResourceStream("PCDoc.Definitions.optimize.json")
                           ?? throw new InvalidOperationException("Embedded optimize catalog missing");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

#pragma warning disable CA1812
    private sealed record CatalogFile(int Version, Item[]? Tweaks);

    private sealed record Item(string Id, TweakKind Kind, RiskLevel Risk, RiskLevel? DesktopRisk, bool Bilan, string[]? Profiles, ValueItem[]? Values,
        string? Plan, bool AbsentIsApplied);

    private sealed record ValueItem(RegistryHive Hive, string? Path, string? Name, int? Dword, string? Text);
#pragma warning restore CA1812
}
