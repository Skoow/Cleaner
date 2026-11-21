using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCDoc.Modules.Updates;

/// <summary>Logiciel à mettre à jour d'après « winget upgrade ».</summary>
public sealed record WingetUpgrade(string Name, string Id, string Version, string Available);

/// <summary>Logiciel du catalogue d'installation groupée.</summary>
public sealed record WingetPackage(string Id, string Name, string Category);

/// <summary>Lecture de la sortie de winget et construction des commandes (identifiants vérifiés).</summary>
public static partial class Winget
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private const string Common = "--exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

    public static string ListUpgradesArguments => "upgrade --accept-source-agreements --disable-interactivity";

    public static string UpgradeArguments(string id) =>
        IsValidId(id) ? $"upgrade --id {id} {Common}" : throw new ArgumentException("Invalid winget id", nameof(id));

    public static string InstallArguments(string id) =>
        IsValidId(id) ? $"install --id {id} {Common} --source winget" : throw new ArgumentException("Invalid winget id", nameof(id));

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Codes de sortie de winget considérés comme réussis (0, ou « aucune mise à jour applicable »).</summary>
    public static bool IsSuccess(int exitCode) => exitCode is 0 or unchecked((int)0x8A15002B);

    /// <summary>
    /// Premier tableau de « winget upgrade » : en-têtes traduits mais ordre fixe (Nom, Id, Version, Disponible, Source).
    /// Les colonnes sont repérées par la position des mots de l'en-tête au-dessus de la ligne de tirets.
    /// </summary>
    public static IReadOnlyList<WingetUpgrade> ParseUpgrades(IEnumerable<string> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var lines = output.Select(Clean).ToList();
        var separator = lines.FindIndex(l => l.StartsWith("---", StringComparison.Ordinal));
        if (separator < 1)
        {
            return [];
        }

        var starts = Columns(lines[separator - 1]);
        if (starts.Count < 4)
        {
            return [];
        }

        var result = new List<WingetUpgrade>();
        foreach (var line in lines.Skip(separator + 1))
        {
            if (line.Length <= starts[3])
            {
                break;
            }

            string Cell(int i) => i + 1 < starts.Count
                ? line[Math.Min(starts[i], line.Length)..Math.Min(starts[i + 1], line.Length)].Trim()
                : line[Math.Min(starts[i], line.Length)..].Trim();
            var id = Cell(1);
            var version = Cell(2);
            var available = Cell(3);
            if (!IsValidId(id) || version.Length == 0 || available.Length == 0 || available.Contains(' ', StringComparison.Ordinal))
            {
                break; // fin du premier tableau (ligne de résumé ou nouveau tableau)
            }

            result.Add(new WingetUpgrade(Cell(0), id, version, available));
        }

        return result;
    }

    public static IReadOnlyList<WingetPackage> LoadCatalog(string json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, Options);
        return [.. (file?.Packages ?? []).Where(p => IsValidId(p.Id) && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new WingetPackage(p.Id!, p.Name!, p.Category ?? "tools"))];
    }

    public static IReadOnlyList<WingetPackage> DefaultCatalog { get; } = LoadEmbedded();

    private static IReadOnlyList<WingetPackage> LoadEmbedded()
    {
        using var stream = typeof(Winget).Assembly.GetManifestResourceStream("PCDoc.Definitions.winget-catalog.json")
                           ?? throw new InvalidOperationException("Embedded winget catalog missing");
        using var reader = new StreamReader(stream);
        return LoadCatalog(reader.ReadToEnd());
    }

    /// <summary>Retire la partie avant le dernier retour chariot (barres de progression) et les caractères de contrôle.</summary>
    private static string Clean(string line)
    {
        var text = line ?? string.Empty;
        var cr = text.LastIndexOf('\r');
        if (cr >= 0)
        {
            text = text[(cr + 1)..];
        }

        return new string([.. text.Where(c => !char.IsControl(c))]).TrimEnd();
    }

    private static List<int> Columns(string header)
    {
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_\-]{0,127}$")]
    private static partial Regex IdPattern();

#pragma warning disable CA1812
    private sealed record CatalogFile(Item[]? Packages);

    private sealed record Item(string? Id, string? Name, string? Category);
#pragma warning restore CA1812
}
