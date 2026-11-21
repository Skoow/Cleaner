using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Apps;

/// <summary>Met en quarantaine le dossier laissé par un programme désinstallé (réversible, supprimé après 7 jours).</summary>
public sealed class LeftoversFixer(AppsEngine engine, IQuarantine quarantine) : IFixer
{
    public const string Kind = "quarantine";

    // Destination choisie à la préparation (écrite dans le journal avant le déplacement).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _planned = new(StringComparer.OrdinalIgnoreCase);

    public string UndoKind => Kind;

    public static Finding FindingFor(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder.ToUpperInvariant())))[..16];
        return new Finding(
            $"apps.leftover.{hash}", AppsModule.Id, Severity.Info,
            new LocalizedText("Finding.Apps.Leftover.Title", folder),
            new LocalizedText("Apps.Explanation.Leftover"),
            folder,
            FixAvailable: true, Risk: RiskLevel.Safe, Reversible: true,
            FixData: JsonSerializer.Serialize(new Data(folder)));
    }

    public bool CanFix(Finding finding) => Parse(finding) is not null;

    public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var folder = Parse(finding) ?? throw new InvalidOperationException("Not a pending leftover");
        var target = _planned[folder] = quarantine.PlanFor(folder);
        var payload = JsonSerializer.Serialize(new Payload(folder, target));
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, payload, "Finding.Apps.Leftover.Title", [folder]));
    }

    public Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var folder = Parse(finding) ?? throw new InvalidOperationException("Not a pending leftover");
        if (!_planned.TryRemove(folder, out var target))
        {
            throw new InvalidOperationException("Leftover not prepared");
        }

        quarantine.Move(folder, target);
        engine.ForgetLeftover(folder);
        return Task.CompletedTask;
    }

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var p = Read(entry);
        return Task.FromResult(p is not null && quarantine.DirectoryExists(p.Target) && !quarantine.DirectoryExists(p.Original));
    }

    public Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var p = Read(entry) ?? throw new InvalidOperationException("Invalid quarantine entry");
        quarantine.Restore(p.Target, p.Original);
        return Task.CompletedTask;
    }

    private static Payload? Read(UndoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            return JsonSerializer.Deserialize<Payload>(entry.Payload) is { Original: { Length: > 0 }, Target: { Length: > 0 } } p ? p : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? Parse(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (string.IsNullOrEmpty(finding.FixData) || finding.FixData.Length > 2048)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Data>(finding.FixData)?.Folder is { } folder && FindingFor(folder).Id == finding.Id
                   && engine.IsPendingLeftover(folder) && quarantine.IsLeftoverCandidate(folder)
                ? folder
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Data(string Folder);

    private sealed record Payload(string Original, string Target);
}
