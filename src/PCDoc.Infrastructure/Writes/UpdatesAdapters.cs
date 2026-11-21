using System.Diagnostics;
using System.IO;
using System.Text;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Windows Update par son API COM officielle (Windows Update Agent) : aucune fenêtre. Les mises à jour qui pourraient
/// demander quelque chose à l'utilisateur (CanRequestUserInput) sont écartées.
/// </summary>
public sealed class WindowsUpdate : IWindowsUpdate
{
    private const string Criteria = "IsInstalled=0 and IsHidden=0";

    public Task<IReadOnlyList<WindowsUpdateItem>> SearchAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var (_, updates) = Search();
        var items = new List<WindowsUpdateItem>();
        for (var i = 0; i < (int)updates.Count; i++)
        {
            var u = updates[i];
            if ((bool)u.InstallationBehavior.CanRequestUserInput)
            {
                continue;
            }

            string? kb = (int)u.KBArticleIDs.Count > 0 ? (string)u.KBArticleIDs[0] : null;
            items.Add(new WindowsUpdateItem((string)u.Identity.UpdateID, (string)u.Title, (int)u.Type == 2, (bool)u.BrowseOnly,
                (long)Convert.ToDecimal(u.MaxDownloadSize, System.Globalization.CultureInfo.InvariantCulture), kb));
        }

        return (IReadOnlyList<WindowsUpdateItem>)items;
    }, cancellationToken);

    public Task<WindowsUpdateResult> InstallAsync(IReadOnlyCollection<string> updateIds, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var wanted = new HashSet<string>(updateIds, StringComparer.OrdinalIgnoreCase);
        var (session, updates) = Search();
        dynamic collection = Create("Microsoft.Update.UpdateColl");
        for (var i = 0; i < (int)updates.Count; i++)
        {
            var u = updates[i];
            if (wanted.Contains((string)u.Identity.UpdateID) && !(bool)u.InstallationBehavior.CanRequestUserInput)
            {
                if (!(bool)u.EulaAccepted)
                {
                    u.AcceptEula(); // l'utilisateur a choisi d'installer cette mise à jour
                }

                collection.Add(u);
            }
        }

        if ((int)collection.Count == 0)
        {
            return new WindowsUpdateResult(0, updateIds.Count, false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var downloader = session.CreateUpdateDownloader();
        downloader.Updates = collection;
        downloader.Download();
        cancellationToken.ThrowIfCancellationRequested();

        var installer = session.CreateUpdateInstaller();
        installer.Updates = collection;
        try
        {
            installer.ForceQuiet = true;
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Ancienne version de l'agent : déjà silencieux pour les mises à jour sans saisie.
        }

        var result = installer.Install();
        var installed = 0;
        for (var i = 0; i < (int)collection.Count; i++)
        {
            // 2 = réussi, 3 = réussi avec erreurs.
            if ((int)result.GetUpdateResult(i).ResultCode is 2 or 3)
            {
                installed++;
            }
        }

        return new WindowsUpdateResult(installed, (int)collection.Count - installed, (bool)result.RebootRequired);
    }, cancellationToken);

    private static (dynamic Session, dynamic Updates) Search()
    {
        dynamic session = Create("Microsoft.Update.Session");
        session.ClientApplicationID = Core.AppInfo.ProductName;
        var searcher = session.CreateUpdateSearcher();
        var result = searcher.Search(Criteria);
        return (session, result.Updates);
    }

    private static dynamic Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)
        ?? throw new InvalidOperationException($"{progId} unavailable");
}

/// <summary>
/// winget.exe du paquet App Installer, trouvé par PowerShell (modules de Windows seulement) puis vérifié : dans
/// WindowsApps, sans lien, modifiable seulement par les administrateurs ou TrustedInstaller. Jamais l'alias du profil.
/// </summary>
public sealed class WingetRunner(IProcessRunner runner) : IWingetRunner, IDisposable
{
    private const string Locate = "$env:PSModulePath = $PSHOME + '\\Modules'\n"
                                  + "Import-Module ($PSHOME + '\\Modules\\Appx') -ErrorAction Stop\n"
                                  + "$p = Get-AppxPackage -AllUsers -Name Microsoft.DesktopAppInstaller | Sort-Object Version -Descending | Select-Object -First 1\n"
                                  + "'PCDOC:' + $p.InstallLocation";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _path;

    public void Dispose() => _gate.Dispose();

    public async Task<(int ExitCode, IReadOnlyList<string> Output)> RunAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var exe = await LocateAsync(cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("winget.exe not found");
        var start = new ProcessStartInfo(exe, arguments ?? string.Empty)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.SystemDirectory,
        };
        var output = new List<string>();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Add(output, e.Data);
        process.ErrorDataReceived += (_, e) => Add(output, e.Data);
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

            throw new TimeoutException($"winget did not finish within {timeout.TotalMinutes:0} minutes");
        }

        lock (output)
        {
            return (process.ExitCode, [.. output]);
        }
    }

    private static void Add(List<string> output, string? line)
    {
        if (line is not null)
        {
            lock (output)
            {
                output.Add(line);
            }
        }
    }

    private async Task<string?> LocateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_path is not null)
            {
                return _path;
            }

            var lines = new List<string>();
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Locate));
            await runner.RunHiddenAsync(@"WindowsPowerShell\v1.0\powershell.exe",
                "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, lines.Add, cancellationToken,
                TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            var folder = lines.LastOrDefault(l => l.StartsWith("PCDOC:", StringComparison.Ordinal))?[6..].Trim();
            var root = Path.Combine(TrustedPaths.ProgramFiles, "WindowsApps");
            if (folder is null || TrustedPaths.Normalize(Path.Combine(folder, "winget.exe")) is not { } exe || !TrustedPaths.IsUnder(exe, root)
                || !File.Exists(exe) || !TrustedPaths.HasNoLinks(exe, root) || !TrustedPaths.OnlyAdminsCanWrite(exe, root))
            {
                return null;
            }

            return _path = exe;
        }
        finally
        {
            _gate.Release();
        }
    }
}
