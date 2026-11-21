using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.TaskScheduler;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure;

/// <summary>Lecture des tâches planifiées via le paquet TaskScheduler (MIT). Lecture seule.</summary>
public sealed class ScheduledTaskReader : IScheduledTaskReader
{
    public IReadOnlyList<ScheduledTaskInfo> ReadAll()
    {
        var results = new List<ScheduledTaskInfo>();
        using var service = new TaskService();
        Walk(service.RootFolder, results);
        return results;
    }

    private static void Walk(TaskFolder folder, List<ScheduledTaskInfo> results)
    {
        try
        {
            foreach (var task in folder.Tasks)
            {
                using (task)
                {
                    var info = TryRead(task);
                    if (info is not null)
                    {
                        results.Add(info);
                    }
                }
            }

            foreach (var sub in folder.SubFolders)
            {
                using (sub)
                {
                    Walk(sub, results);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or FileNotFoundException)
        {
            // Dossier inaccessible : ignoré (journalisé par l'appelant si besoin).
        }
    }

    private static ScheduledTaskInfo? TryRead(Microsoft.Win32.TaskScheduler.Task task)
    {
        try
        {
            var definition = task.Definition;
            var commands = new List<string>();
            foreach (var action in definition.Actions)
            {
                switch (action)
                {
                    case ExecAction exec:
                        var path = (exec.Path ?? string.Empty).Trim().Trim('"');
                        commands.Add(string.IsNullOrWhiteSpace(exec.Arguments) ? $"\"{path}\"" : $"\"{path}\" {exec.Arguments}");
                        break;
                    case ComHandlerAction com:
                        commands.Add($"COM {com.ClassId:B}".ToUpperInvariant());
                        break;
                    default:
                        commands.Add(action.ActionType.ToString());
                        break;
                }
            }

            return new ScheduledTaskInfo(
                task.Path,
                task.Name,
                task.Enabled,
                definition.RegistrationInfo.Author,
                commands,
                string.Join("; ", definition.Triggers.Select(t => t.ToString(CultureInfo.CurrentUICulture))),
                ToOffset(task.LastRunTime),
                ToOffset(task.NextRunTime));
        }
#pragma warning disable CA1031 // Une tâche illisible est ignorée ; elle ne doit pas interrompre la lecture des autres.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static DateTimeOffset? ToOffset(DateTime value) =>
        value <= new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified) || value == DateTime.MaxValue
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local));
}
