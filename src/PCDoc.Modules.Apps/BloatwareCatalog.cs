using System.Text.Json;
using System.Text.Json.Serialization;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Apps;

/// <summary>Raison d'une entrée du catalogue : publicité, rarement utile, outil du fabricant, à garder.</summary>
public enum BloatReason
{
    Promo,
    Unused,
    Oem,
    Keep,
}

/// <summary>Entrée du catalogue : <paramref name="Pattern"/> = nom du paquet (Store) ou début du nom affiché (Win32).</summary>
public sealed record BloatEntry(AppKind Kind, string Pattern, RiskLevel Risk, BloatReason Reason)
{
    /// <summary>Vrai pour une application préinstallée à proposer au retrait (pas une entrée « à garder »).</summary>
    public bool IsBloat => Reason != BloatReason.Keep;
}

/// <summary>Catalogue des applications préinstallées et des éléments à protéger (Definitions/bloatware.json).</summary>
public sealed class BloatwareCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private BloatwareCatalog(IReadOnlyList<BloatEntry> entries) => Entries = entries;

    public static BloatwareCatalog Default { get; } = LoadEmbedded();

    public IReadOnlyList<BloatEntry> Entries { get; }

    public static BloatwareCatalog Load(string json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, Options) ?? throw new JsonException("Empty bloatware catalog");
        var entries = new List<BloatEntry>();
        foreach (var e in file.Entries ?? [])
        {
            if (!string.IsNullOrWhiteSpace(e.Store))
            {
                entries.Add(new BloatEntry(AppKind.Store, e.Store.Trim(), e.Risk, e.Reason));
            }
            else if (!string.IsNullOrWhiteSpace(e.Win32))
            {
                entries.Add(new BloatEntry(AppKind.Win32, e.Win32.Trim(), e.Risk, e.Reason));
            }
        }

        return new BloatwareCatalog(entries);
    }

    /// <summary>
    /// Entrée correspondante : la plus précise l'emporte (motif le plus long), et à longueur égale la plus prudente.
    /// Store : nom exact du paquet, « * » joker. Win32 : le nom affiché commence par le motif.
    /// </summary>
    public BloatEntry? Match(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var name = app.Kind == AppKind.Store ? StoreName(app) : app.Name;
        return Entries
            .Where(e => e.Kind == app.Kind && (app.Kind == AppKind.Store ? Wildcard(e.Pattern, name) : name.StartsWith(e.Pattern, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Pattern.Replace("*", string.Empty, StringComparison.Ordinal).Length)
            .ThenByDescending(e => (int)e.Risk)
            .FirstOrDefault();
    }

    /// <summary>Nom du paquet (avant le premier « _ » du nom de famille).</summary>
    internal static string StoreName(InstalledApp app) =>
        (app.PackageFamilyName ?? app.Id) is var family && family.StartsWith("store:", StringComparison.Ordinal) ? family[6..].Split('_')[0] : family.Split('_')[0];

    internal static bool Wildcard(string pattern, string text)
    {
        var parts = pattern.Split('*');
        if (parts.Length == 1)
        {
            return pattern.Equals(text, StringComparison.OrdinalIgnoreCase);
        }

        if (!text.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) || !text.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase)
            || text.Length < parts[0].Length + parts[^1].Length)
        {
            return false;
        }

        var position = parts[0].Length;
        var end = text.Length - parts[^1].Length;
        foreach (var middle in parts[1..^1])
        {
            var found = text.IndexOf(middle, position, end - position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            position = found + middle.Length;
        }

        return true;
    }

    private static BloatwareCatalog LoadEmbedded()
    {
        using var stream = typeof(BloatwareCatalog).Assembly.GetManifestResourceStream("PCDoc.Definitions.bloatware.json")
                           ?? throw new InvalidOperationException("Embedded bloatware catalog missing");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

#pragma warning disable CA1812
    private sealed record CatalogFile(int Version, Item[]? Entries);

    private sealed record Item(string? Store, string? Win32, RiskLevel Risk, BloatReason Reason);
#pragma warning restore CA1812
}
