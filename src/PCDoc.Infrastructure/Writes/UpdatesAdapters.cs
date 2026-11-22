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

    // Catégorie « Upgrades » : passage à une nouvelle version de Windows, jamais proposé ici.
    private const string UpgradesCategory = "3689bdc8-b205-4af4-8d4a-a63924c5e9d5";

    private static readonly Guid SessionClsid = new("4CB43D7F-7EEE-4906-8698-60DA1C38F2FE");
    private static readonly Guid CollectionClsid = new("13639463-00DB-4646-803D-528026140D88");
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

    private readonly Lock _gate = new();
    private (dynamic Session, dynamic Updates, DateTime At)? _last;

    public async Task<IReadOnlyList<WindowsUpdateItem>> SearchAsync(CancellationToken cancellationToken)
    {
        var (_, updates) = await SearchWithTimeoutAsync(fresh: true, cancellationToken).ConfigureAwait(false);
        var items = new List<WindowsUpdateItem>();
        for (var i = 0; i < (int)updates.Count; i++)
        {
            var u = updates[i];
            if ((bool)u.InstallationBehavior.CanRequestUserInput || IsUpgrade(u))
            {
                continue;
            }

            string? kb = (int)u.KBArticleIDs.Count > 0 ? (string)u.KBArticleIDs[0] : null;
            items.Add(new WindowsUpdateItem((string)u.Identity.UpdateID, (string)u.Title, (int)u.Type == 2, (bool)u.BrowseOnly,
                (long)Convert.ToDecimal(u.MaxDownloadSize, System.Globalization.CultureInfo.InvariantCulture), kb,
                Exclusive: (int)u.InstallationBehavior.Impact == 2));
        }

        return items;
    }

    private static bool IsUpgrade(dynamic update)
    {
        for (var i = 0; i < (int)update.Categories.Count; i++)
        {
            if (string.Equals((string)update.Categories[i].CategoryID, UpgradesCategory, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Recherche (plusieurs minutes possibles) réutilisée 30 min : installer plusieurs mises à jour ne relance pas tout.</summary>
    private async Task<(dynamic Session, dynamic Updates)> SearchWithTimeoutAsync(bool fresh, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!fresh && _last is { } cached && DateTime.UtcNow - cached.At < CacheLifetime)
            {
                return (cached.Session, cached.Updates);
            }
        }

        var search = Task.Run(Search, cancellationToken);
        var done = await Task.WhenAny(search, Task.Delay(SearchTimeout, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (done != search)
        {
            throw new TimeoutException("Windows Update search did not finish within 10 minutes");
        }

        var result = await search.ConfigureAwait(false);
        lock (_gate)
        {
            _last = (result.Session, result.Updates, DateTime.UtcNow);
        }

        return result;
    }

    public async Task<WindowsUpdateResult> InstallAsync(IReadOnlyCollection<string> updateIds, CancellationToken cancellationToken)
    {
        var (session, updates) = await SearchWithTimeoutAsync(fresh: false, cancellationToken).ConfigureAwait(false);
        var result = await Task.Run(() => Install(session, updates, updateIds, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (result.RebootRequired)
        {
            lock (_gate)
            {
                _last = null; // après un redémarrage demandé, la liste n'est plus fiable
            }
        }

        return result;
    }

    private static WindowsUpdateResult Install(dynamic session, dynamic updates, IReadOnlyCollection<string> updateIds, CancellationToken cancellationToken)
    {
        var wanted = new HashSet<string>(updateIds, StringComparer.OrdinalIgnoreCase);
        dynamic collection = Create(CollectionClsid);
        for (var i = 0; i < (int)updates.Count; i++)
        {
            var u = updates[i];
            if (wanted.Contains((string)u.Identity.UpdateID) && !(bool)u.InstallationBehavior.CanRequestUserInput && !(bool)u.IsInstalled
                && !IsUpgrade(u))
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
        var download = downloader.Download();
        if ((int)download.ResultCode is not (2 or 3))
        {
            throw new InvalidOperationException($"Windows Update download failed (code {(int)download.ResultCode})");
        }

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
    }

    private static (dynamic Session, dynamic Updates) Search()
    {
        dynamic session = Create(SessionClsid);
        session.ClientApplicationID = Core.AppInfo.ProductName;
        var searcher = session.CreateUpdateSearcher();
        var result = searcher.Search(Criteria);
        return (session, result.Updates);
    }

    /// <summary>Par CLSID (pas par nom) : un nom de programme peut être redéfini dans le registre de l'utilisateur.</summary>
    private static dynamic Create(Guid clsid) =>
        Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!)
        ?? throw new InvalidOperationException($"{clsid} unavailable");
}

/// <summary>
/// winget.exe du paquet App Installer, trouvé par PowerShell (modules de Windows seulement) puis vérifié : dans
/// WindowsApps, sans lien, modifiable seulement par les administrateurs ou TrustedInstaller. Jamais l'alias du profil.
/// </summary>
public sealed class WingetRunner(IProcessRunner runner) : IWingetRunner, IDisposable
{
    private const string Locate = "$env:PSModulePath = $PSHOME + '\\Modules'\n"
                                  + "Import-Module ($PSHOME + '\\Modules\\Appx') -ErrorAction Stop\n"
                                  + "$p = Get-AppxPackage -AllUsers -Name Microsoft.DesktopAppInstaller | Where-Object { $_.PackageFamilyName -eq 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -and $_.SignatureKind -ne 'Developer' } | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1\n"
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

        // Dossier temporaire réservé aux administrateurs : l'installateur téléchargé ne peut pas être remplacé entre la
        // vérification de son empreinte par winget et son lancement.
        // Un sous-dossier par exécution : deux winget en même temps ne suppriment jamais les fichiers l'un de l'autre.
        var temp = Path.Combine(SecureFolders.EnsureChild("temp"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        start.Environment["TEMP"] = temp;
        start.Environment["TMP"] = temp;
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

        // Téléchargements de winget supprimés dès la fin (ils ne servent qu'à l'installation).
        Purge(temp);
        lock (output)
        {
            return (process.ExitCode, [.. output]);
        }
    }

    private static void Purge(string temp)
    {
        try
        {
            CleanupFileSystem.DeleteTree(temp, SecureFolders.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier encore ouvert : supprimé au prochain passage.
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

/// <summary>
/// Copie des pilotes tiers installés (pnputil /export-driver *) dans %ProgramData%\PCDoc\drivers\aaaaMMjj-HHmmss.
/// Restauration : Gestionnaire de périphériques > Mettre à jour le pilote > Parcourir ce dossier.
/// </summary>
public sealed class DriverBackup(IProcessRunner runner, IClock clock) : IDriverBackup
{
    public async Task<string> BackupAsync(CancellationToken cancellationToken)
    {
        var root = SecureFolders.EnsureChild("drivers");
        var free = new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
        if (free < 5L * 1024 * 1024 * 1024)
        {
            throw new IOException("Less than 5 GB free: driver backup skipped");
        }

        var folder = Path.Combine(root, clock.Now.ToLocalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var exit = await runner.RunHiddenAsync("pnputil.exe", $"/export-driver * \"{folder}\"", null, cancellationToken, TimeSpan.FromMinutes(20))
            .ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"pnputil /export-driver exited with code {exit}");
        }

        // Seules les 2 dernières sauvegardes sont gardées (une exportation peut peser plusieurs Go).
        foreach (var old in new DirectoryInfo(root).EnumerateDirectories().OrderByDescending(d => d.Name, StringComparer.Ordinal).Skip(2))
        {
            try
            {
                CleanupFileSystem.DeleteTree(old.FullName, SecureFolders.Root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nouvel essai à la prochaine sauvegarde.
            }
        }

        return folder;
    }
}

/// <summary>
/// Mise à jour des applications du Store par le fournisseur officiel de gestion des appareils
/// (MDM_EnterpriseModernAppManagement_AppManagement01.UpdateScanMethod), via PowerShell limité aux modules de Windows.
/// </summary>
public sealed class StoreUpdates(IProcessRunner runner) : IStoreUpdates
{
    private const string Script = "$env:PSModulePath = $PSHOME + '\\Modules'\n"
                                  + "Import-Module ($PSHOME + '\\Modules\\CimCmdlets') -ErrorAction Stop\n"
                                  + "$ErrorActionPreference = 'Stop'\n"
                                  + "try { Get-CimInstance -Namespace 'root\\cimv2\\mdm\\dmmap' -ClassName 'MDM_EnterpriseModernAppManagement_AppManagement01' | "
                                  + "Invoke-CimMethod -MethodName UpdateScanMethod | Out-Null; exit 0 } catch { Write-Output $_.Exception.Message; exit 1 }";

    public async Task TriggerAsync(CancellationToken cancellationToken)
    {
        var output = new List<string>();
        var exit = await runner.RunHiddenAsync(@"WindowsPowerShell\v1.0\powershell.exe",
            "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)),
            output.Add, cancellationToken, TimeSpan.FromMinutes(3)).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"Store update scan failed: {string.Join(' ', output).Trim()}");
        }
    }
}
