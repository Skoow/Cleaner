using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Apps;

/// <summary>Une application avec son niveau de risque, la raison du catalogue et la méthode de désinstallation.</summary>
public sealed record AppItem(InstalledApp App, RiskLevel Risk, BloatEntry? Entry, UninstallPlan? Plan)
{
    public bool IsBloat => Entry?.IsBloat == true;

    public bool CanUninstall => Plan is not null && Risk != RiskLevel.Locked;
}

public sealed record AppsReport(IReadOnlyList<AppItem> Apps, IReadOnlyList<string> Leftovers);

/// <summary>Source de la page Applications (et du Bilan).</summary>
public interface IAppsSource
{
    Task<AppsReport> AnalyzeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Inventaire, niveaux de risque et désinstallation. Le dernier inventaire sert à revalider chaque correction : une
/// correction ne vise jamais une application absente ou dont le niveau de risque a été minimisé.
/// </summary>
public sealed class AppsEngine(
    Win32AppReader win32,
    StoreAppReader store,
    BloatwareCatalog catalog,
    IUninstallLauncher launcher,
    IProcessRunner runner,
    IQuarantine quarantine,
    IUserProfileResolver profiles,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IAppsSource
{
    private static readonly TimeSpan MsiTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan ExeTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StoreTimeout = TimeSpan.FromMinutes(10);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Lock _gate = new();
    private readonly List<string> _leftovers = [];
    private Dictionary<string, AppItem> _items = new(StringComparer.Ordinal);

    public async Task<AppsReport> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var apps = new List<InstalledApp>(await Task.Run(win32.Read, cancellationToken).ConfigureAwait(false));
        try
        {
            apps.AddRange(await store.ReadAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or System.ComponentModel.Win32Exception)
        {
            // PowerShell indisponible : seuls les programmes Win32 sont listés.
        }

        var items = apps.Select(Classify).OrderBy(i => i.App.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        lock (_gate)
        {
            _items = items.GroupBy(i => i.App.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _leftovers.RemoveAll(path => !quarantine.DirectoryExists(path));
            return new AppsReport(items, [.. _leftovers]);
        }
    }

    public AppItem Classify(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var entry = catalog.Match(app);
        var plan = UninstallPlanner.Plan(app, launcher);
        var risk = app.Kind == AppKind.Store && (app.NonRemovable || app.IsFramework) ? RiskLevel.Locked : entry?.Risk ?? RiskLevel.Safe;
        return new AppItem(app, risk, entry, plan);
    }

    public AppItem? Find(string id)
    {
        lock (_gate)
        {
            return _items.GetValueOrDefault(id);
        }
    }

    public bool IsPendingLeftover(string path)
    {
        lock (_gate)
        {
            return _leftovers.Contains(path, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void ForgetLeftover(string path)
    {
        lock (_gate)
        {
            _leftovers.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Désinstalle puis vérifie que le programme a vraiment disparu ; note le dossier restant éventuel.</summary>
    public async Task UninstallAsync(AppItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var plan = item.Plan ?? throw new InvalidOperationException("No uninstall method for this application");
        var app = item.App;
        switch (plan.Method)
        {
            case UninstallMethod.Msi:
                var msi = await runner.RunHiddenAsync("msiexec.exe", plan.Arguments, null, cancellationToken, MsiTimeout).ConfigureAwait(false);
                if (msi is not (0 or 1605 or 1614 or 1641 or 3010))
                {
                    throw new InvalidOperationException($"msiexec.exe exited with code {msi}");
                }

                break;
            case UninstallMethod.Silent or UninstallMethod.Interactive:
                await launcher.RunAsync(plan.Executable!, plan.Arguments, plan.Method == UninstallMethod.Silent, ExeTimeout, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case UninstallMethod.Store:
                await RemoveStoreAsync(app, cancellationToken).ConfigureAwait(false);
                return;
        }

        // Beaucoup de désinstalleurs se relancent depuis une copie temporaire et rendent la main tout de suite.
        var wait = plan.Method == UninstallMethod.Interactive ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(2);
        for (var waited = TimeSpan.Zero; win32.StillInstalled(app); waited += TimeSpan.FromSeconds(2))
        {
            if (waited >= wait)
            {
                throw new InvalidOperationException("The application is still installed (uninstall cancelled or failed)");
            }

            await _delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        RecordLeftover(app);
    }

    private async Task RemoveStoreAsync(InstalledApp app, CancellationToken cancellationToken)
    {
        var profile = profiles.Resolve();
        var user = profile.IsProcessUser || !StoreAppReader.IsSid(profile.Sid) ? string.Empty : $" -User '{profile.Sid}'";
        var lines = new List<string> { "$ErrorActionPreference = 'Stop'", "try {" };
        if (StoreAppReader.IsSafeName(app.PackageFullName))
        {
            lines.Add($"  Remove-AppxPackage -Package '{app.PackageFullName}'{user}");
        }

        // Une application préinstallée est aussi retirée de l'image : elle ne revient pas pour les nouveaux comptes.
        if (StoreAppReader.IsSafeName(app.ProvisionedName))
        {
            lines.Add($"  Remove-AppxProvisionedPackage -Online -PackageName '{app.ProvisionedName}' | Out-Null");
        }

        if (lines.Count == 2)
        {
            throw new InvalidOperationException("Invalid package name");
        }

        lines.Add("  exit 0");
        lines.Add("} catch { Write-Output $_.Exception.Message; exit 1 }");
        var output = new List<string>();
        var exit = await runner.RunHiddenAsync(StoreAppReader.PowerShell, StoreAppReader.Arguments(string.Join('\n', lines)), output.Add,
            cancellationToken, StoreTimeout).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"Remove-AppxPackage failed: {string.Join(' ', output).Trim()}");
        }
    }

    /// <summary>Dossier d'installation encore présent, propre à ce programme (aucun autre programme installé dedans ou au-dessus).</summary>
    private void RecordLeftover(InstalledApp app)
    {
        var folder = app.InstallLocation
                     ?? (UninstallPlanner.Split(app.UninstallString) is { } cmd && cmd.Exe.LastIndexOf('\\') is > 2 and var slash ? cmd.Exe[..slash] : null);
        if (string.IsNullOrWhiteSpace(folder) || !quarantine.IsLeftoverCandidate(folder) || !quarantine.DirectoryExists(folder))
        {
            return;
        }

        lock (_gate)
        {
            var shared = _items.Values.Any(i => i.App.Id != app.Id && i.App.InstallLocation is { } other
                                               && (IsInside(other, folder) || IsInside(folder, other)));
            if (!shared && !_leftovers.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                _leftovers.Add(folder);
            }
        }
    }

    internal static bool IsInside(string path, string folder)
    {
        var p = path.TrimEnd('\\') + "\\";
        var f = folder.TrimEnd('\\') + "\\";
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}
