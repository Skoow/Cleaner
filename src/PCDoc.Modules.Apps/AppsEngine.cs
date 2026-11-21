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
        var used = await Task.Run(win32.UsedFolders, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _items = items.GroupBy(i => i.App.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            // Un reste réinstallé (ou réutilisé par un autre programme) n'est plus un reste.
            _leftovers.RemoveAll(path => !quarantine.DirectoryExists(path) || Overlaps(path, used));
            return new AppsReport(items, [.. _leftovers]);
        }
    }

    public AppItem Classify(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var entry = catalog.Match(app);
        var plan = UninstallPlanner.Plan(app, launcher);
        if (plan?.Method == UninstallMethod.Store && app.PackageFullName is null && entry?.IsBloat != true)
        {
            plan = null; // paquet provisionné seul et hors catalogue : on ne retire pas l'image de Windows
        }

        // Paquet Microsoft absent du catalogue : peut être un composant de Windows (langue, codec…) → 🟠, jamais en mode novice.
        var unknownDefault = app.Kind == AppKind.Store && (app.Publisher?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ?? true)
            ? RiskLevel.Conditional
            : RiskLevel.Safe;
        var risk = app.Kind == AppKind.Store && (app.NonRemovable || app.IsFramework) ? RiskLevel.Locked : entry?.Risk ?? unknownDefault;
        return new AppItem(app, risk, entry, plan);
    }

    public AppItem? Find(string id)
    {
        lock (_gate)
        {
            return _items.GetValueOrDefault(id);
        }
    }

    /// <summary>Reste noté et toujours inutilisé (vérifié à nouveau dans le registre au moment de la correction).</summary>
    public bool IsPendingLeftover(string path)
    {
        lock (_gate)
        {
            if (!_leftovers.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return !Overlaps(path, win32.UsedFolders());
    }

    private static bool Overlaps(string folder, IEnumerable<string> used) =>
        used.Any(u => IsInside(u, folder) || IsInside(folder, u));

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
                await RemoveStoreAsync(item, cancellationToken).ConfigureAwait(false);
                return;
        }

        // Beaucoup de désinstalleurs se relancent depuis une copie temporaire et rendent la main tout de suite.
        var wait = TimeSpan.FromMinutes(10);
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

    private async Task RemoveStoreAsync(AppItem item, CancellationToken cancellationToken)
    {
        var app = item.App;
        var user = StoreAppReader.UserArgument(profiles.Resolve());
        var name = BloatwareCatalog.StoreName(app);
        var lines = new List<string> { "$ErrorActionPreference = 'Stop'", "try {" };
        if (StoreAppReader.IsSafeName(app.PackageFullName) && StoreAppReader.IsSafeName(name))
        {
            lines.Add($"  Remove-AppxPackage -Package '{app.PackageFullName}'{user}");
            lines.Add($"  if (Get-AppxPackage -Name '{name}'{user}) {{ Write-Output 'Still installed'; exit 2 }}");
        }

        // Une application préinstallée du catalogue est aussi retirée de l'image : elle ne revient pas pour les nouveaux comptes.
        if (item.IsBloat && StoreAppReader.IsSafeName(app.ProvisionedName))
        {
            lines.Add($"  Remove-AppxProvisionedPackage -Online -PackageName '{app.ProvisionedName}' | Out-Null");
        }

        if (lines.Count == 2)
        {
            throw new InvalidOperationException("Invalid package name");
        }

        lines[0] = StoreAppReader.Preamble + lines[0];

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
        // Sans InstallLocation, le dossier du désinstalleur n'est retenu qu'à deux niveaux au moins sous Program Files
        // (« Program Files\Éditeur\Application ») : jamais le dossier commun d'un éditeur.
        if (app.InstallLocation is null && folder is not null && folder.Count(c => c == '\\') < 3)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(folder) || !quarantine.IsLeftoverCandidate(folder) || !quarantine.DirectoryExists(folder)
            || Overlaps(folder, win32.UsedFolders()))
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
