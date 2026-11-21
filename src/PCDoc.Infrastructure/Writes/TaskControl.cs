using Microsoft.Win32.TaskScheduler;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>Activation des tâches planifiées (paquet TaskScheduler, MIT).</summary>
public sealed class TaskControl : ITaskControl
{
    public bool? IsEnabled(string taskPath)
    {
        using var service = new TaskService();
        using var task = service.GetTask(taskPath);
        return task?.Enabled;
    }

    public void SetEnabled(string taskPath, bool enabled)
    {
        using var service = new TaskService();
        using var task = service.GetTask(taskPath) ?? throw new InvalidOperationException($"Task {taskPath} not found");
        task.Enabled = enabled;
    }
}
