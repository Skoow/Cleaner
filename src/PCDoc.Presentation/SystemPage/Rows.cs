using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Inventory;

namespace PCDoc.Presentation.SystemPage;

/// <summary>Une ligne « libellé : valeur » d'une carte. <paramref name="State"/> : pastille pour les contrôles Windows 11.</summary>
public sealed record InfoLine(string Label, string Value, CheckState? State = null);

/// <summary>Une carte d'informations (Vue d'ensemble, Matériel). <paramref name="Gauge"/> : jauge de 0 à 1.</summary>
public sealed record InfoCard(
    string Title, string Glyph, IReadOnlyList<InfoLine> Lines, double? Gauge = null, bool GaugeWarning = false, string? Note = null);

/// <summary>Une barre du graphique des démarrages (<paramref name="Ratio"/> = durée / durée maximale).</summary>
public sealed record BootBar(double Ratio, bool IsLast, string Tooltip);

public sealed record DegradationRow(string Name, string Kind, string Delay);

public sealed record DiskRow(string Name, string Type, string Size);

public sealed record VolumeRow(string Letter, string FileSystem, string Size, string Free, bool Warning);

/// <summary>Pastille de signature : toujours un mot + une couleur.</summary>
public enum SignatureBadge
{
    Signed,
    Unsigned,
    Missing,
    None,
}

/// <summary>Une ligne des listes Démarrage / Tâches / Services.</summary>
public sealed record AutorunRow(
    AutorunEntry Entry,
    string Name,
    string Publisher,
    string KindLabel,
    string Location,
    string CommandLine,
    SignatureBadge Badge,
    string SignatureLabel,
    string StateLabel)
{
    public bool Enabled => Entry.Enabled;

    public bool IsUnsigned => Badge is SignatureBadge.Unsigned or SignatureBadge.Missing;

    public string Details => $"{Location}\n{CommandLine}\n{Entry.FilePath}";

    /// <summary>Niveau de risque du conseil (Sûr, Selon l'usage, Déconseillé, Verrouillé).</summary>
    public string AdviceLabel { get; init; } = string.Empty;

    public PCDoc.Core.Findings.RiskLevel? AdviceLevel { get; init; }

    public string AdviceReason { get; init; } = string.Empty;

    public string ToggleLabel { get; init; } = string.Empty;

    public bool CanToggle { get; init; }

    public PCDoc.Core.Findings.Finding? ToggleFinding { get; init; }
}
