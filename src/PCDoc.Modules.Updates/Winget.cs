using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCDoc.Modules.Updates;

/// <summary>Logiciel à mettre à jour d'après « winget upgrade ».</summary>
public sealed record WingetUpgrade(string Name, string Id, string Version, string Available, string? Source = null);

/// <summary>Logiciel du catalogue d'installation groupée.</summary>
public sealed record WingetPackage(string Id, string Name, string Category);

/// <summary>Lecture de la sortie de winget et construction des commandes (identifiants vérifiés).</summary>
public static partial class Winget
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private const string Common = "--exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

    /// <summary>
    /// Seulement les logiciels installés pour toute la machine : leurs entrées (HKLM) ne peuvent être créées que par un
    /// administrateur, donc un compte standard ne peut pas faire installer un paquet au choix par l'outil.
    /// </summary>
    public static string ListUpgradesArguments => "upgrade --scope machine --accept-source-agreements --disable-interactivity";

    public static string UpgradeArguments(string id, string? source = null) =>
        IsValidId(id)
            ? $"upgrade --id {id} {Common} --scope machine" + (source is "winget" or "msstore" ? $" --source {source}" : string.Empty)
            : throw new ArgumentException("Invalid winget id", nameof(id));

    public static string InstallArguments(string id) =>
        IsValidId(id) ? $"install --id {id} {Common} --scope machine --source winget" : throw new ArgumentException("Invalid winget id", nameof(id));

    /// <summary>Installé, mais un redémarrage est nécessaire.</summary>
    public static bool NeedsReboot(int exitCode) => exitCode is unchecked((int)0x8A150109) or unchecked((int)0x8A15010A);

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Codes de sortie de winget considérés comme réussis (0, ou « aucune mise à jour applicable »).</summary>
    public static bool IsSuccess(int exitCode) => exitCode is 0 or unchecked((int)0x8A15002B) || NeedsReboot(exitCode);

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
            // Fin du premier tableau : ligne vide, ou ligne de tirets d'un second tableau.
            if (line.Trim().Length == 0 || line.StartsWith("---", StringComparison.Ordinal))
            {
                break;
            }

            string Cell(int i) => Slice(line, starts[i], i + 1 < starts.Count ? starts[i + 1] : int.MaxValue).Trim();
            var id = Cell(1);
            var version = Cell(2);
            var available = Cell(3);
            if (!IsValidId(id) || version.Length == 0 || available.Length == 0 || available.Contains(' ', StringComparison.Ordinal))
            {
                continue; // identifiant tronqué (« … ») ou ligne de résumé : ignorée, les suivantes restent lues
            }

            result.Add(new WingetUpgrade(Cell(0), id, version, available, starts.Count > 4 ? Cell(4) : null));
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

    /// <summary>Débuts de colonnes en cellules d'affichage (un caractère chinois, japonais ou coréen occupe deux cellules).</summary>
    private static List<int> Columns(string header)
    {
        var starts = new List<int>();
        var cell = 0;
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
            {
                starts.Add(cell);
            }

            cell += Width(header[i]);
        }

        return starts;
    }

    /// <summary>Partie de la ligne entre deux positions en cellules d'affichage.</summary>
    private static string Slice(string line, int startCell, int endCell)
    {
        var text = new System.Text.StringBuilder();
        var cell = 0;
        foreach (var c in line)
        {
            if (cell >= startCell && cell < endCell)
            {
                text.Append(c);
            }

            cell += Width(c);
        }

        return text.ToString();
    }

    private static int Width(char c) => c is >= '\u1100' and <= '\u115F' or >= '\u2E80' and <= '\uA4CF' or >= '\uAC00' and <= '\uD7A3'
        or >= '\uF900' and <= '\uFAFF' or >= '\uFE30' and <= '\uFE4F' or >= '\uFF00' and <= '\uFF60' or >= '\uFFE0' and <= '\uFFE6' ? 2 : 1;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_\-]{0,127}$")]
    private static partial Regex IdPattern();

#pragma warning disable CA1812
    private sealed record CatalogFile(Item[]? Packages);

    private sealed record Item(string? Id, string? Name, string? Category);
#pragma warning restore CA1812
}
