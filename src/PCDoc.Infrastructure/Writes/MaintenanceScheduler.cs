using System.Globalization;
using System.IO;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Entretien automatique par le Planificateur de tâches (schtasks), avec les droits les plus élevés pour l'utilisateur
/// actuel : l'UAC n'est demandé qu'une fois, à la création. La tâche ne vise que PCDoc installé dans Program Files
/// (dossier réservé aux administrateurs) : un exe modifiable par l'utilisateur serait lancé élevé sans confirmation.
/// </summary>
public sealed class MaintenanceScheduler(IProcessRunner runner, IRemovableDrives drives) : IMaintenanceScheduler
{
    public const string TaskName = @"PCDoc\Maintenance";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    private static string InstallFolder => Path.Combine(TrustedPaths.ProgramFiles, "PCDoc");

    public bool IsScheduled() => Run($"/Query /TN \"{TaskName}\"") == 0;

    public string? BlockedReason()
    {
        var exe = InstalledExe();
        return exe is null ? "Maintenance.NotInstalled" : null;
    }

    public void Schedule(DayOfWeek day, int hour)
    {
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour));
        }

        var exe = InstalledExe() ?? throw new InvalidOperationException("PCDoc is not installed in Program Files");
        var days = day switch
        {
            DayOfWeek.Monday => "MON", DayOfWeek.Tuesday => "TUE", DayOfWeek.Wednesday => "WED", DayOfWeek.Thursday => "THU",
            DayOfWeek.Friday => "FRI", DayOfWeek.Saturday => "SAT", _ => "SUN",
        };
        var exit = Run($"/Create /F /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" --maintenance\" /SC WEEKLY /D {days} " +
                       $"/ST {hour.ToString("00", CultureInfo.InvariantCulture)}:00 /RL HIGHEST /IT");
        if (exit != 0)
        {
            throw new InvalidOperationException($"schtasks /Create exited with code {exit}");
        }
    }

    public void Unschedule() => Run($"/Delete /F /TN \"{TaskName}\"");

    public string InstallToProgramFiles()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable");
        var source = Path.GetDirectoryName(exe)!;
        if (!Path.GetFullPath(source).TrimEnd('\\').Equals(InstallFolder, StringComparison.OrdinalIgnoreCase))
        {
            SecureFolders.RemoveLinkIfAny(InstallFolder);
            Copy(source, InstallFolder);
        }

        return InstalledExe() ?? throw new InvalidOperationException("Copy to Program Files failed");
    }

    public string CopyPortable(string removableRoot)
    {
        if (!drives.IsRemovableRoot(removableRoot))
        {
            throw new InvalidOperationException("Not a removable drive");
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable");
        var target = Path.Combine(removableRoot, "PCDoc");
        Copy(Path.GetDirectoryName(exe)!, target);
        File.WriteAllText(Path.Combine(target, "portable.txt"), "PCDoc portable: settings and technician profiles are stored next to PCDoc.exe.");
        return target;
    }

    /// <summary>PCDoc.exe dans Program Files\PCDoc, sans lien et modifiable seulement par les administrateurs.</summary>
    private static string? InstalledExe()
    {
        var name = Path.GetFileName(Environment.ProcessPath ?? "PCDoc.exe");
        var exe = Path.Combine(InstallFolder, name);
        var root = TrustedPaths.ProgramFiles;
        return File.Exists(exe) && TrustedPaths.HasNoLinks(exe, root) && TrustedPaths.OnlyAdminsCanWrite(exe, root) ? exe : null;
    }

    /// <summary>Copie l'application (sans les données du mode portable ni les journaux).</summary>
    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("PCDoc-data", StringComparison.OrdinalIgnoreCase) || relative.Equals("portable.txt", StringComparison.OrdinalIgnoreCase)
                || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private int Run(string arguments) =>
        runner.RunHiddenAsync("schtasks.exe", arguments, null, CancellationToken.None, Timeout).GetAwaiter().GetResult();
}
