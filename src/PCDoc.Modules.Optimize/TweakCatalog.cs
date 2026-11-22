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

/// <summary>
/// Valeur cible : int (DWORD) ou string (REG_SZ, comparée sans tenir compte de la casse).
/// <paramref name="Acceptable"/> : autres valeurs jugées correctes (ex. UAC plus strict que la valeur par défaut).
/// </summary>
public sealed record TweakValue(RegistryHive Hive, string Path, string Name, object Target, IReadOnlyList<object>? Acceptable = null)
{
    public bool IsSatisfiedBy(object? current) =>
        current is not null && (Same(current, Target) || (Acceptable?.Any(a => Same(current, a)) ?? false));

    private static bool Same(object a, object b) =>
        a is string sa && b is string sb ? sa.Trim().Equals(sb, StringComparison.OrdinalIgnoreCase) : Equals(a, b);
}

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
    bool AbsentIsApplied = false,
    string Module = "optimize",
    string TextPrefix = "Optimize",
    Severity Severity = Severity.Info);

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

    public static TweakCatalog Load(string json) => Load(json, "optimize", "Optimize");

    /// <param name="module">Module des problèmes produits (préfixe des identifiants).</param>
    /// <param name="textPrefix">Préfixe des clés de traduction (« Optimize.id.Title »).</param>
    public static TweakCatalog Load(string json, string module, string textPrefix)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, Options) ?? throw new JsonException("Empty optimize catalog");
        var tweaks = new List<Tweak>();
        foreach (var t in file.Tweaks ?? [])
        {
            var values = (t.Values ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v.Path) && !string.IsNullOrWhiteSpace(v.Name) && (v.Dword is not null) != (v.Text is not null))
                .Select(v => new TweakValue(v.Hive, v.Path!, v.Name!, v.Dword is { } d ? d : v.Text!,
                    [.. (v.AcceptableDwords ?? []).Select(a => (object)a), .. (v.AcceptableTexts ?? [])]))
                .ToList();
            var valid = t.Kind switch
            {
                TweakKind.Registry => values.Count > 0,
                TweakKind.PowerPlan => Guid.TryParse(t.Plan, out _),
                _ => true,
            };
            if (valid && !string.IsNullOrWhiteSpace(t.Id) && t.Risk is not RiskLevel.Locked)
            {
                tweaks.Add(new Tweak(t.Id, t.Kind, t.Risk, t.DesktopRisk, t.Bilan, t.Profiles ?? [], values, t.Plan?.ToLowerInvariant(), t.AbsentIsApplied,
                    module, textPrefix, t.Severity ?? Severity.Info));
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
        string? Plan, bool AbsentIsApplied, Severity? Severity);

    private sealed record ValueItem(RegistryHive Hive, string? Path, string? Name, int? Dword, string? Text, int[]? AcceptableDwords, string[]? AcceptableTexts);
#pragma warning restore CA1812
}
