namespace PCDoc.Core.Abstractions;

/// <summary>Une tâche planifiée. <paramref name="ExecCommands"/> : « "chemin" arguments », ou « COM {CLSID} ».</summary>
public sealed record ScheduledTaskInfo(
    string Path,
    string Name,
    bool Enabled,
    string? Author,
    IReadOnlyList<string> ExecCommands,
    string Triggers,
    DateTimeOffset? LastRun,
    DateTimeOffset? NextRun);

/// <summary>Lecture des tâches planifiées (dossiers inaccessibles ignorés).</summary>
public interface IScheduledTaskReader
{
    IReadOnlyList<ScheduledTaskInfo> ReadAll();
}
