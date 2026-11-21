using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;
using PCDoc.Modules.System.Advisor;
using PCDoc.Modules.System.Autoruns;

namespace PCDoc.Modules.System.Fixes;

/// <summary>
/// Active ou désactive un programme au démarrage (StartupApproved, comme le Gestionnaire des tâches),
/// un service (type de démarrage) ou une tâche planifiée. Toujours réversible ; .reg sauvegardé avant.
/// </summary>
public sealed class AutorunToggleFixer(
    IRegistryReader reader,
    IRegistryWriter writer,
    IServiceControl services,
    ITaskControl tasks,
    IBackupStore backups,
    Func<DateTimeOffset> now,
    AutorunAdvisor advisor) : IFixer
{
    public const string Kind = "system.toggle";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";

    public string UndoKind => Kind;

    public bool CanFix(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return ToggleRequest.Parse(finding.FixData) is { } r && IsWellFormed(r) && IsAllowed(r, finding.Risk);
    }

    private static bool IsWellFormed(ToggleRequest r) =>
        r.Kind is AutorunKind.Startup or AutorunKind.Service or AutorunKind.ScheduledTask
        && !string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 512
        && (r.Kind != AutorunKind.Startup || (r.Hive is not null && r.ApprovedSubKey is "Run" or "Run32" or "StartupFolder"));

    /// <summary>Le niveau est recalculé : jamais 🔒, et jamais plus risqué que ce que le problème annonçait.</summary>
    private bool IsAllowed(ToggleRequest r, RiskLevel claimed)
    {
        var level = advisor.LevelFor(r);
        return level != RiskLevel.Locked && (int)claimed >= (int)level;
    }

    private ToggleRequest Guarded(Finding finding)
    {
        var request = ToggleRequest.Parse(finding.FixData);
        if (request is null || !IsWellFormed(request) || !IsAllowed(request, finding.Risk))
        {
            throw new InvalidOperationException("Change refused: locked or riskier than announced");
        }

        return request;
    }

    /// <summary>Annulation : le journal est une donnée lue sur le disque, on revérifie tout.</summary>
    private (ToggleRequest Request, State Before) GuardedPayload(UndoEntry entry)
    {
        var (request, before) = ReadPayload(entry);
        if (!IsWellFormed(request) || advisor.LevelFor(request) == RiskLevel.Locked
            || before.ServiceMode is ServiceStartMode.Boot or ServiceStartMode.System or ServiceStartMode.Unknown
            || before.StartupValue is { Length: > 128 })
        {
            throw new InvalidOperationException("Undo refused: invalid or locked target");
        }

        return (request, before);
    }

    public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var request = Guarded(finding);
        var before = new State();
        switch (request.Kind)
        {
            case AutorunKind.Startup:
                var path = ApprovedPath(request);
                var value = reader.GetValue(request.Hive!.Value, path, request.Name) as byte[];
                before = before with { StartupValue = value is null ? null : Convert.ToBase64String(value) };
                backups.SaveRegistryBackup(BackupName("startup", request.Name),
                    RegFile.Format(writer.FullPath(request.Hive.Value, path), [(request.Name, value)]));
                break;
            case AutorunKind.Service:
                before = before with { ServiceMode = services.GetStartMode(request.Name) };
                var key = $@"{ServicesRoot}\{request.Name}";
                backups.SaveRegistryBackup(BackupName("service", request.Name),
                    RegFile.Format(writer.FullPath(RegistryHive.LocalMachine, key),
                    [("Start", reader.GetValue(RegistryHive.LocalMachine, key, "Start")),
                     ("DelayedAutostart", reader.GetValue(RegistryHive.LocalMachine, key, "DelayedAutostart"))]));
                break;
            default:
                before = before with { TaskEnabled = tasks.IsEnabled(request.Name) ?? throw new InvalidOperationException("Task not found") };
                break;
        }

        var payload = JsonSerializer.Serialize(new Payload(request, before));
        var titleKey = request.Action == ToggleAction.Disable ? "Undo.Disabled" : "Undo.Enabled";
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, payload, titleKey, [request.Name]));
    }

    public Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var request = Guarded(finding);
        var disable = request.Action == ToggleAction.Disable;
        switch (request.Kind)
        {
            case AutorunKind.Startup:
                writer.SetBinary(request.Hive!.Value, ApprovedPath(request), request.Name, ApprovedValue(disable));
                break;
            case AutorunKind.Service:
                services.SetStartMode(request.Name, disable ? ServiceStartMode.Disabled : ServiceStartMode.Manual);
                break;
            default:
                tasks.SetEnabled(request.Name, !disable);
                break;
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        ToggleRequest request;
        try
        {
            (request, _) = GuardedPayload(entry);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            return Task.FromResult(false);
        }

        var disable = request.Action == ToggleAction.Disable;
        var unchanged = request.Kind switch
        {
            AutorunKind.Startup => reader.GetValue(request.Hive!.Value, ApprovedPath(request), request.Name) is byte[] { Length: > 0 } v
                                   && ((v[0] & 1) == 1) == disable,
            AutorunKind.Service => services.GetStartMode(request.Name) == (disable ? ServiceStartMode.Disabled : ServiceStartMode.Manual),
            _ => tasks.IsEnabled(request.Name) == !disable,
        };
        return Task.FromResult(unchanged);
    }

    public Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var (request, before) = GuardedPayload(entry);
        switch (request.Kind)
        {
            case AutorunKind.Startup:
                if (before.StartupValue is null)
                {
                    writer.DeleteValue(request.Hive!.Value, ApprovedPath(request), request.Name);
                }
                else
                {
                    writer.SetBinary(request.Hive!.Value, ApprovedPath(request), request.Name, Convert.FromBase64String(before.StartupValue));
                }

                break;
            case AutorunKind.Service:
                services.SetStartMode(request.Name, before.ServiceMode ?? ServiceStartMode.Manual);
                break;
            default:
                tasks.SetEnabled(request.Name, before.TaskEnabled ?? true);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>Format du Gestionnaire des tâches : 12 octets, octet 0 = 0x02 activé / 0x03 désactivé (+ date).</summary>
    private byte[] ApprovedValue(bool disable)
    {
        var value = new byte[12];
        value[0] = disable ? (byte)0x03 : (byte)0x02;
        if (disable)
        {
            BitConverter.GetBytes(now().ToFileTime()).CopyTo(value, 4);
        }

        return value;
    }

    private static string ApprovedPath(ToggleRequest r) => $@"{ApprovedRoot}\{r.ApprovedSubKey}";

    private string BackupName(string kind, string name) =>
        $"{now():yyyyMMdd-HHmmss}-{kind}-{string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'))}.reg";

    private static Payload ReadPayload(UndoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return JsonSerializer.Deserialize<Payload>(entry.Payload) ?? throw new InvalidOperationException("Invalid undo payload");
    }

    private sealed record State(string? StartupValue = null, ServiceStartMode? ServiceMode = null, bool? TaskEnabled = null);

    private sealed record Payload(ToggleRequest Request, State Before)
    {
        public void Deconstruct(out ToggleRequest request, out State before) => (request, before) = (Request, Before);
    }
}
