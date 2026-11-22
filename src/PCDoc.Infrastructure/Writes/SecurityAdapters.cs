using System.Diagnostics;
using System.IO;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>MpCmdRun.exe de Program Files\Windows Defender (vérifié : sans lien, modifiable seulement par les administrateurs).</summary>
public sealed class DefenderCommand : IDefenderCommand
{
    public async Task<int> RunAsync(string arguments, Action<string>? onOutput, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var root = TrustedPaths.ProgramFiles;
        var exe = Path.Combine(root, "Windows Defender", "MpCmdRun.exe");
        if (!File.Exists(exe) || !TrustedPaths.HasNoLinks(exe, root) || !TrustedPaths.OnlyAdminsCanWrite(exe, root))
        {
            throw new FileNotFoundException("MpCmdRun.exe not found or not protected");
        }

        var start = new ProcessStartInfo(exe, arguments ?? string.Empty)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Forward(e.Data, onOutput);
        process.ErrorDataReceived += (_, e) => Forward(e.Data, onOutput);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Déjà terminé.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"MpCmdRun did not finish within {timeout.TotalMinutes:0} minutes");
        }

        return process.ExitCode;
    }

    private static void Forward(string? line, Action<string>? onOutput)
    {
        var text = line?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        if (!string.IsNullOrEmpty(text))
        {
            onOutput?.Invoke(text);
        }
    }
}

/// <summary>Lecteurs amovibles prêts ; seules les entrées placées directement à leur racine sont lues ou modifiées.</summary>
public sealed class RemovableDrives : IRemovableDrives
{
    public IReadOnlyList<RemovableDrive> List() =>
        [.. DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Select(d => new RemovableDrive(d.RootDirectory.FullName, Label(d)))];

    public bool IsRemovableRoot(string root) =>
        root is { Length: 3 } && root[1] == ':' && root[2] == '\\' && List().Any(d => d.Root.Equals(root, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<RootItem> RootItems(string root)
    {
        if (!IsRemovableRoot(root))
        {
            return [];
        }

        try
        {
            return [.. new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    ReturnSpecialDirectories = false,
                })
                .Select(i => new RootItem(i.FullName, i is DirectoryInfo, i.Attributes))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void SetAttributes(string path, FileAttributes attributes)
    {
        Check(path);
        File.SetAttributes(path, attributes);
    }

    public void Rename(string path, string newPath)
    {
        Check(path);
        Check(newPath, mustExist: false);
        File.Move(path, newPath);
    }

    private void Check(string path, bool mustExist = true)
    {
        var root = path.Length > 3 ? path[..3] : string.Empty;
        if (!IsRemovableRoot(root) || path[3..].Contains('\\', StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal)
            || (mustExist && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidOperationException("Only items at the root of a removable drive can be changed");
        }
    }

    private static string? Label(DriveInfo drive)
    {
        try
        {
            return string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
