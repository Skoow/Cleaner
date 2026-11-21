using System.Globalization;
using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Cleanup;

/// <summary>Nettoie une catégorie (suppression définitive, non annulable par nature), vide la corbeille ou lance DISM.</summary>
public sealed class CleanupFixer(CleanupEngine engine, IProcessRunner runner) : IFixer
{
    public const string Kind = "cleanup";

    public event EventHandler<long>? Freed;

    public string UndoKind => Kind;

    public bool SupportsUndo => false;

    public static Finding FindingFor(string categoryId, RiskLevel risk, long bytes = 0, int files = 0) => new(
        $"cleanup.{categoryId}", CleanupModule.Id, Severity.Info,
        new LocalizedText("Finding.Cleanup.Title", new LocalizedText($"Cleanup.{categoryId}.Title"), bytes / (double)(1L << 30)),
        new LocalizedText($"Cleanup.{categoryId}.Explanation"),
        string.Create(CultureInfo.InvariantCulture, $"{categoryId}: {bytes:N0} bytes, {files} files"),
        FixAvailable: true, Risk: risk, Reversible: false,
        FixData: JsonSerializer.Serialize(new Data(categoryId)));

    public bool CanFix(Finding finding) =>
        Parse(finding) is { } id && (int)finding.Risk >= (int)engine.RiskOf(id) && engine.RiskOf(id) != RiskLevel.Locked;

    public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var id = Guarded(finding);
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, id, $"Cleanup.{id}.Title", []));
    }

    public async Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var id = Guarded(finding);
        if (id == CleanupEngine.ComponentStoreId)
        {
            var exit = await runner.RunHiddenAsync("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", null, cancellationToken,
                TimeSpan.FromHours(2)).ConfigureAwait(false);
            if (exit is not (0 or 3010))
            {
                throw new InvalidOperationException($"dism.exe exited with code {exit}");
            }

            return;
        }

        var freed = await Task.Run(() => engine.Clean(id), cancellationToken).ConfigureAwait(false);
        Freed?.Invoke(this, freed);
    }

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException("Deleted files cannot be restored");

    private string Guarded(Finding finding) =>
        CanFix(finding) ? Parse(finding)! : throw new InvalidOperationException("Cleanup refused: unknown category or understated risk");

    private string? Parse(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (string.IsNullOrEmpty(finding.FixData) || finding.FixData.Length > 256)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Data>(finding.FixData)?.Category is { } id && engine.IsKnown(id) && finding.Id == $"cleanup.{id}"
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Data(string Category);
}
