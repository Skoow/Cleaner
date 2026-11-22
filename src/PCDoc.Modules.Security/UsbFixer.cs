using System.Collections.Concurrent;
using System.Text.Json;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Security;

/// <summary>
/// Répare une clé infectée par un « virus raccourci » : les raccourcis qui lancent un script sont neutralisés (renommés
/// en « .lnk.pcdoc-disabled ») et les dossiers cachés à la racine redeviennent visibles. Tout est réversible.
/// L'analyse Defender de la clé reste conseillée (bouton à côté).
/// </summary>
public sealed class UsbFixer(SecurityEngine engine) : SecurityFixerBase<UsbFixer.Data>("security.usb")
{
    public const string DisabledSuffix = ".pcdoc-disabled";

    private readonly ConcurrentDictionary<string, Plan> _planned = new(StringComparer.OrdinalIgnoreCase);

    public static Finding FindingFor(UsbDrive drive)
    {
        ArgumentNullException.ThrowIfNull(drive);
        return new Finding($"security.usb.{drive.Drive.Root[0]}", SecurityModule.Id, Severity.Critical,
            new LocalizedText("Security.usb.Title", drive.Drive.Root, drive.Drive.Label ?? string.Empty), new LocalizedText("Security.usb.Explanation"),
            string.Join(Environment.NewLine, drive.SuspiciousShortcuts.Concat(drive.HiddenFolders.Select(h => h.Path + " (hidden)"))),
            FixAvailable: true, Risk: RiskLevel.Safe, Reversible: true, FixData: JsonSerializer.Serialize(new Data(drive.Drive.Root)));
    }

    public override Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Guarded(finding);
        var drive = engine.ScanDrive(data.Root) ?? throw new InvalidOperationException("Drive removed");
        var plan = new Plan(data.Root,
            [.. drive.HiddenFolders.Select(h => new Folder(h.Path, (int)h.Attributes))],
            [.. drive.SuspiciousShortcuts.Where(s => !engine.Drives.RootItems(data.Root).Any(i => i.Path.Equals(s + DisabledSuffix, StringComparison.OrdinalIgnoreCase)))
                .Select(s => new Renamed(s, s + DisabledSuffix))]);
        _planned[data.Root] = plan;
        return Task.FromResult(UndoEntry.Create(finding.Id, UndoKind, Json(plan), "Security.usb.Title", [data.Root, drive.Drive.Label ?? string.Empty]));
    }

    public override Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Guarded(finding);
        if (!_planned.TryRemove(data.Root, out var plan))
        {
            throw new InvalidOperationException("USB repair not prepared");
        }

        // En cas d'échec en cours de route (clé retirée, fichier verrouillé), ce qui a été fait est défait : l'entrée
        // marquée « échouée » ne laisse donc jamais la clé à moitié modifiée.
        var renamed = new List<Renamed>();
        var changed = new List<Folder>();
        try
        {
            foreach (var r in plan.Shortcuts)
            {
                engine.Drives.Rename(r.From, r.To);
                renamed.Add(r);
            }

            foreach (var f in plan.Folders)
            {
                engine.Drives.SetAttributes(f.Path, (FileAttributes)f.Attributes & ~(FileAttributes.Hidden | FileAttributes.System));
                changed.Add(f);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Rollback(renamed, changed);
            throw;
        }

        return Task.CompletedTask;
    }

    private void Rollback(List<Renamed> renamed, List<Folder> changed)
    {
        foreach (var r in renamed)
        {
            TryRun(() => engine.Drives.Rename(r.To, r.From));
        }

        foreach (var f in changed)
        {
            TryRun(() => engine.Drives.SetAttributes(f.Path, (FileAttributes)f.Attributes));
        }
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Clé retirée : rien de plus à faire.
        }
    }

    public override Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var plan = Read(entry);
        var items = engine.Drives.RootItems(plan.Root).ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(plan.Shortcuts.All(r => items.ContainsKey(r.To) && !items.ContainsKey(r.From))
                               && plan.Folders.All(f => items.TryGetValue(f.Path, out var i) && !i.Attributes.HasFlag(FileAttributes.Hidden)));
    }

    public override Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var plan = Read(entry);
        foreach (var r in plan.Shortcuts)
        {
            engine.Drives.Rename(r.To, r.From);
        }

        foreach (var f in plan.Folders)
        {
            engine.Drives.SetAttributes(f.Path, (FileAttributes)f.Attributes);
        }

        return Task.CompletedTask;
    }

    protected override Data? Validate(Finding finding, Data data) =>
        data.Root is { Length: 3 } && engine.IsRemovableRoot(data.Root) && finding.Id == $"security.usb.{data.Root[0]}" && engine.ScanDrive(data.Root) is { IsInfected: true } ? data : null;

    /// <summary>Le journal est revérifié : uniquement des éléments placés directement à la racine d'un lecteur amovible branché.</summary>
    private Plan Read(UndoEntry entry)
    {
        var plan = Payload<Plan>(entry);
        bool Direct(string path) => path.StartsWith(plan.Root, StringComparison.OrdinalIgnoreCase) && path.Length > plan.Root.Length
                                    && !path[plan.Root.Length..].Contains('\\', StringComparison.Ordinal) && !path.Contains("..", StringComparison.Ordinal);
        var ok = engine.IsRemovableRoot(plan.Root)
                 && plan.Folders.All(f => Direct(f.Path))
                 && plan.Shortcuts.All(r => Direct(r.From) && r.To == r.From + DisabledSuffix && r.From.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));
        return ok ? plan : throw new InvalidOperationException("Invalid undo entry or drive not plugged in");
    }

    public sealed record Data(string Root);

    private sealed record Folder(string Path, int Attributes);

    private sealed record Renamed(string From, string To);

    private sealed record Plan(string Root, List<Folder> Folders, List<Renamed> Shortcuts);
}
