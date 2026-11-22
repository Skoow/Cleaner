using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Updates;

public sealed record UpdatesReport(
    IReadOnlyList<WindowsUpdateItem> Windows,
    IReadOnlyList<WingetUpgrade> Software,
    bool WindowsUpdateFailed,
    bool WingetMissing,
    bool WingetFailed = false);

public interface IUpdatesSource
{
    /// <param name="includeWindows">Faux pour le Bilan : la recherche Windows Update peut durer plusieurs minutes.</param>
    Task<UpdatesReport> AnalyzeAsync(bool includeWindows, CancellationToken cancellationToken);

    IReadOnlyList<WingetPackage> Catalog { get; }
}

/// <summary>
/// Recherche et installation. Les derniers résultats servent à revalider chaque correction : on n'installe que ce qui a
/// été trouvé (Windows Update, winget upgrade) ou ce qui figure au catalogue (installation groupée).
/// </summary>
public sealed class UpdatesEngine(IWindowsUpdate windowsUpdate, IWingetRunner winget, IReadOnlyList<WingetPackage>? catalog = null,
    IDriverBackup? driverBackup = null) : IUpdatesSource
{
    private Task<string>? _driverBackup;

    /// <summary>Dossier de la sauvegarde des pilotes faite pendant cette session (null si aucune).</summary>
    public string? DriverBackupFolder { get; private set; }

    /// <summary>Appelé après chaque sauvegarde des pilotes (dossier), pour le journal d'activité.</summary>
    public Action<string>? DriversBackedUp { get; set; }

    /// <summary>Sauvegarde des pilotes, une seule fois par session (réutilisée pour les pilotes suivants).</summary>
    public async Task<string?> BackupDriversAsync(CancellationToken cancellationToken)
    {
        if (driverBackup is null)
        {
            return null;
        }

        _driverBackup ??= driverBackup.BackupAsync(cancellationToken);
        try
        {
            DriverBackupFolder = await _driverBackup.ConfigureAwait(false);
            DriversBackedUp?.Invoke(DriverBackupFolder);
            return DriverBackupFolder;
        }
        catch
        {
            _driverBackup = null; // nouvel essai au prochain pilote
            throw;
        }
    }

    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    private readonly Lock _gate = new();
    private Dictionary<string, WindowsUpdateItem> _windows = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, WingetUpgrade> _software = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WingetPackage> Catalog { get; } = catalog ?? Winget.DefaultCatalog;

    /// <summary>Vrai si une mise à jour installée demande un redémarrage.</summary>
    public bool RebootRequired { get; private set; }

    public async Task<UpdatesReport> AnalyzeAsync(bool includeWindows, CancellationToken cancellationToken)
    {
        IReadOnlyList<WindowsUpdateItem> windows = [];
        var windowsFailed = false;
        if (includeWindows)
        {
            try
            {
                windows = await windowsUpdate.SearchAsync(cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Windows Update peut échouer de bien des façons (COM, droits, délai) : signalé, jamais bloquant
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                windowsFailed = true;
            }
#pragma warning restore CA1031
        }

        IReadOnlyList<WingetUpgrade> software = [];
        var missing = false;
        var wingetFailed = false;
        try
        {
            var (exit, output) = await winget.RunAsync(Winget.ListUpgradesArguments, ListTimeout, cancellationToken).ConfigureAwait(false);
            software = Winget.ParseUpgrades(output);
            wingetFailed = software.Count == 0 && !Winget.IsSuccess(exit);
        }
        catch (FileNotFoundException)
        {
            missing = true;
        }
#pragma warning disable CA1031 // winget qui ne démarre pas ou dépasse le délai : signalé comme un échec
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            wingetFailed = true;
        }
#pragma warning restore CA1031

        lock (_gate)
        {
            if (includeWindows && !windowsFailed)
            {
                _windows = windows.GroupBy(w => w.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }

            if (!missing && !wingetFailed)
            {
                _software = software.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
        }

        return new UpdatesReport(windows, software, windowsFailed, missing, wingetFailed);
    }

    public static RiskLevel RiskOf(WindowsUpdateItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.IsDriver || item.IsOptional || item.Exclusive ? RiskLevel.Conditional : RiskLevel.Safe;
    }

    public WindowsUpdateItem? FindWindows(string id)
    {
        lock (_gate)
        {
            return _windows.GetValueOrDefault(id);
        }
    }

    public WingetUpgrade? FindUpgrade(string id)
    {
        lock (_gate)
        {
            return _software.GetValueOrDefault(id);
        }
    }

    public WingetPackage? FindPackage(string id) => Catalog.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task InstallWindowsAsync(WindowsUpdateItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsDriver)
        {
            // Avant tout nouveau pilote : copie des pilotes actuels (retour arrière possible même sans point de restauration).
            await BackupDriversAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = await windowsUpdate.InstallAsync([item.Id], cancellationToken).ConfigureAwait(false);
        RebootRequired |= result.RebootRequired;
        if (result.Installed == 0 || result.Failed > 0)
        {
            throw new InvalidOperationException($"Windows Update could not install {item.Title}");
        }
    }

    public Task UpgradeAsync(WingetUpgrade upgrade, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        return RunAsync(Winget.UpgradeArguments(upgrade.Id, upgrade.Source), cancellationToken);
    }

    public Task InstallAsync(WingetPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        return RunAsync(Winget.InstallArguments(package.Id), cancellationToken);
    }

    private async Task RunAsync(string arguments, CancellationToken cancellationToken)
    {
        var (exit, output) = await winget.RunAsync(arguments, InstallTimeout, cancellationToken).ConfigureAwait(false);
        RebootRequired |= Winget.NeedsReboot(exit);
        if (!Winget.IsSuccess(exit))
        {
            throw new InvalidOperationException($"winget exited with code 0x{exit:X8}: {output.LastOrDefault(l => l.Trim().Length > 0)?.Trim()}");
        }
    }
}
