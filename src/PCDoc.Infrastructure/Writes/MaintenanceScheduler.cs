using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Entretien automatique par le Planificateur de tâches, avec les droits les plus élevés (l'UAC n'est demandé qu'à la
/// création). Protections, car la tâche peut être lancée sans confirmation :
/// <list type="bullet">
/// <item>elle ne vise que PCDoc installé dans Program Files (dossier réservé aux administrateurs) ;</item>
/// <item>elle passe par cmd.exe /d (sans « AutoRun » du profil) qui force le dossier d'extraction de .NET vers un
/// dossier protégé et vide les variables de « startup hooks » : rien ne peut être chargé depuis un dossier de l'utilisateur ;</item>
/// <item>la copie dans Program Files ne prend que PCDoc.exe et les bibliothèques de WPF signées par Microsoft.</item>
/// </list>
/// </summary>
public sealed class MaintenanceScheduler(IProcessRunner runner, IRemovableDrives drives, ISignatureVerifier signatures) : IMaintenanceScheduler
{
    public const string TaskName = @"PCDoc\Maintenance";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    /// <summary>Bibliothèques natives de WPF éventuellement placées à côté de l'exe (signées par Microsoft).</summary>
    private static readonly string[] NativeLibraries =
        ["wpfgfx_cor3.dll", "PresentationNative_cor3.dll", "D3DCompiler_47_cor3.dll", "vcruntime140_cor3.dll", "PenImc_cor3.dll"];

    private static string InstallFolder => Path.Combine(TrustedPaths.ProgramFiles, "PCDoc");

    public bool IsScheduled() => Run($"/Query /TN \"{TaskName}\"") == 0;

    public string? BlockedReason() => InstalledExe() is null ? "Maintenance.NotInstalled" : null;

    public void Schedule(DayOfWeek day, int hour)
    {
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour));
        }

        var exe = InstalledExe() ?? throw new InvalidOperationException("PCDoc is not installed in Program Files");
        var extract = SecureFolders.EnsureChild("runtime");
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var arguments = $"/d /c \"set DOTNET_BUNDLE_EXTRACT_BASE_DIR={extract}&& set DOTNET_STARTUP_HOOKS=&& set COMPlus_DbgEnableMiniDump=&& \"{exe}\" --maintenance\"";
        var start = DateTime.Today.AddDays(((int)day - (int)DateTime.Today.DayOfWeek + 7) % 7).AddHours(hour);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>PCDoc automatic maintenance</Description></RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}</StartBoundary>
                  <ScheduleByWeek><DaysOfWeek><{day} /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>
                </CalendarTrigger>
              </Triggers>
              <Principals><Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT6H</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(cmd)}</Command><Arguments>{SecurityElement.Escape(arguments)}</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(SecureFolders.EnsureChild("temp"), $"task-{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            var exit = Run($"/Create /F /TN \"{TaskName}\" /XML \"{file}\"");
            if (exit != 0)
            {
                throw new InvalidOperationException($"schtasks /Create exited with code {exit}");
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    public void Unschedule() => Run($"/Delete /F /TN \"{TaskName}\"");

    public string InstallToProgramFiles()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable");
        if (!Path.GetDirectoryName(Path.GetFullPath(exe))!.TrimEnd('\\').Equals(InstallFolder, StringComparison.OrdinalIgnoreCase))
        {
            SecureFolders.RemoveLinkIfAny(InstallFolder);
            CopyApplication(exe, InstallFolder, portable: false);
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
        CopyApplication(exe, target, portable: true);
        WriteNoLink(Path.Combine(target, "portable.txt"), "PCDoc portable: settings and technician profiles are stored next to PCDoc.exe.");
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

    /// <summary>
    /// Liste fermée : l'exe, ses licences et les bibliothèques natives de WPF signées par Microsoft. Jamais le reste du
    /// dossier de départ (souvent « Téléchargements », où une DLL piégée pourrait attendre).
    /// </summary>
    private void CopyApplication(string exe, string target, bool portable)
    {
        if (Directory.Exists(target) && File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The target folder is a link");
        }

        Directory.CreateDirectory(target);
        var source = Path.GetDirectoryName(exe)!;
        CopyNoLink(exe, Path.Combine(target, Path.GetFileName(exe)));
        foreach (var name in NativeLibraries)
        {
            var file = Path.Combine(source, name);
            if (File.Exists(file) && signatures.Verify(file) is { Status: SignatureStatus.Signed, Publisher: "Microsoft Corporation" })
            {
                CopyNoLink(file, Path.Combine(target, name));
            }
        }

        foreach (var file in new DirectoryInfo(source).EnumerateFiles("LICENSE*").Concat(new DirectoryInfo(source).EnumerateFiles("THIRD-PARTY*")))
        {
            if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint) && file.Length < 4 * 1024 * 1024)
            {
                CopyNoLink(file.FullName, Path.Combine(target, file.Name));
            }
        }

        _ = portable;
    }

    /// <summary>Copie sans jamais écrire à travers un lien placé à la destination.</summary>
    private static void CopyNoLink(string from, string to)
    {
        if (File.Exists(to) && File.GetAttributes(to).HasFlag(FileAttributes.ReparsePoint))
        {
            File.Delete(to); // supprime le lien lui-même
        }

        File.Copy(from, to, overwrite: true);
    }

    private static void WriteNoLink(string path, string text)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            File.Delete(path);
        }

        File.WriteAllText(path, text);
    }

    private int Run(string arguments) =>
        runner.RunHiddenAsync("schtasks.exe", arguments, null, CancellationToken.None, Timeout).GetAwaiter().GetResult();
}
