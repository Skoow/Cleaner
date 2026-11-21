using System.Text.Json;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Apps;

/// <summary>
/// Désinstalle une application (non annulable : le point de restauration de la série reste le filet). La correction est
/// revalidée contre le dernier inventaire : application présente, méthode connue, niveau de risque non minimisé.
/// </summary>
public sealed class UninstallFixer(AppsEngine engine) : IFixer
{
    public const string Kind = "uninstall";

    public string UndoKind => Kind;

    public bool SupportsUndo => false;

    public static Finding FindingFor(AppItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var explanation = item.Plan?.Method switch
        {
            UninstallMethod.Interactive => "Apps.Explanation.Interactive",
            UninstallMethod.Store when item.App.ProvisionedName is not null => "Apps.Explanation.Provisioned",
            _ when item.Entry is { } e => $"Apps.Reason.{e.Reason}",
            _ => "Apps.Explanation.Silent",
        };
        return new Finding(
            $"apps.{item.App.Id}", AppsModule.Id, Severity.Info,
            new LocalizedText("Finding.Apps.Uninstall.Title", item.App.Name),
            new LocalizedText(explanation),
            $"{item.App.Id} ({item.Plan?.Method.ToString() ?? "none"})",
            FixAvailable: item.CanUninstall, Risk: item.Risk, Reversible: false, EstimatedGain: null,
            FixData: JsonSerializer.Serialize(new Data(item.App.Id)));
    }

    public bool CanFix(Finding finding) => Resolve(finding) is not null;

    public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var item = Guarded(finding);
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, item.App.Id, "Finding.Apps.Uninstall.Title", [item.App.Name]));
    }

    public Task ApplyAsync(Finding finding, CancellationToken cancellationToken) => engine.UninstallAsync(Guarded(finding), cancellationToken);

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException("An uninstall cannot be undone");

    private AppItem Guarded(Finding finding) =>
        Resolve(finding) ?? throw new InvalidOperationException("Uninstall refused: unknown application or understated risk");

    private AppItem? Resolve(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (string.IsNullOrEmpty(finding.FixData) || finding.FixData.Length > 1024)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Data>(finding.FixData)?.App is { } id && finding.Id == $"apps.{id}"
                   && engine.Find(id) is { CanUninstall: true } item && (int)finding.Risk >= (int)item.Risk
                ? item
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Data(string App);
}
