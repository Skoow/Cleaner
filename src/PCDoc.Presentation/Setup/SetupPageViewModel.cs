using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Apps;
using PCDoc.Modules.Cleanup;
using PCDoc.Modules.Optimize;
using PCDoc.Modules.Security;
using PCDoc.Modules.Updates;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Setup;

/// <summary>Une étape de la préparation (cochable) avec les corrections qu'elle contient.</summary>
public sealed partial class SetupStep(string id, string title, string description) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _summary = string.Empty;

    public string Id { get; } = id;

    public string Title { get; } = title;

    public string Description { get; } = description;

    public List<Finding> Findings { get; } = [];
}

/// <summary>Logiciel proposé à l'installation pendant la préparation.</summary>
public sealed partial class SetupSoftware(WingetPackage package, string category) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public WingetPackage Package { get; } = package;

    public string Name => Package.Name;

    public string Category { get; } = category;
}

/// <summary>Les sources de chaque module utilisées par la préparation.</summary>
public sealed record SetupSources(
    IOptimizeSource Privacy,
    ISecuritySource Security,
    IOptimizeSource Optimize,
    IAppsSource Apps,
    IUpdatesSource Updates,
    ICleanupSource Cleanup);

/// <summary>
/// Page « Nouveau PC » : prépare un PC en une fois, dans le bon ordre et avec un seul point de restauration —
/// confidentialité, sécurité, optimisation (profil au choix), applications préinstallées, mises à jour de Windows et
/// des logiciels, installation de logiciels, nettoyage. Seules les corrections proposées dans le mode actuel sont
/// incluses (🟢 en mode novice).
/// </summary>
public sealed partial class SetupPageViewModel : ObservableObject
{
    private readonly SetupSources _sources;
    private readonly ILocalizer _localizer;
    private readonly Func<Core.Settings.AppMode> _mode;
    private bool _loaded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _profile = "office";

    [ObservableProperty]
    private string? _status;

    public SetupPageViewModel(SetupSources sources, ILocalizer localizer, FixConfirmationViewModel? fixes, Func<Core.Settings.AppMode>? mode = null)
    {
        _sources = sources;
        _localizer = localizer;
        _mode = mode ?? (() => Core.Settings.AppMode.Novice);
        Fixes = fixes;
        if (fixes is not null)
        {
            fixes.Completed += (_, _) => Status = _localizer["Setup.Done"];
        }

        Software = [.. sources.Updates.Catalog.Select(p => new SetupSoftware(p, localizer[$"Updates.Category.{p.Category}"]))];
    }

    public FixConfirmationViewModel? Fixes { get; }

    public ObservableCollection<SetupStep> Steps { get; } = [];

    public IReadOnlyList<SetupSoftware> Software { get; }

    public IReadOnlyList<Optimize.ProfileChoice> Profiles =>
        [.. TweakCatalog.ProfileIds.Select(p => new Optimize.ProfileChoice(p, _localizer[$"Optimize.Profile.{p}"]))];

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : AnalyzeAsync();

    partial void OnProfileChanged(string value) => _ = AnalyzeAsync();

    [RelayCommand]
    private void SelectProfile(string? profile)
    {
        if (profile is not null && TweakCatalog.ProfileIds.Contains(profile))
        {
            Profile = profile;
        }
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        Status = _localizer["Setup.Analyzing"];
        try
        {
            // Les analyses sont indépendantes : lancées en même temps (Windows Update est la plus longue).
            var privacy = Safe(() => _sources.Privacy.AnalyzeAsync(CancellationToken.None), []);
            var security = Safe<SecurityReport?>(async () => await _sources.Security.AnalyzeAsync(CancellationToken.None).ConfigureAwait(false), null);
            var optimize = Safe(() => _sources.Optimize.AnalyzeAsync(CancellationToken.None), []);
            var apps = Safe<AppsReport?>(async () => await _sources.Apps.AnalyzeAsync(CancellationToken.None).ConfigureAwait(false), null);
            var updates = Safe<UpdatesReport?>(async () => await _sources.Updates.AnalyzeAsync(includeWindows: true, CancellationToken.None).ConfigureAwait(false), null);
            var cleanup = Safe<CleanupReport?>(() => Task.Run<CleanupReport?>(_sources.Cleanup.Analyze), null);
            await Task.WhenAll(privacy, security, optimize, apps, updates, cleanup).ConfigureAwait(true);

            Steps.Clear();
            Add("privacy", privacy.Result.Where(s => s.Available && !s.Applied && s.Tweak.Profiles.Contains("recommended")).Select(TweakFixer.FindingFor));
            Add("security", security.Result?.Findings.Where(f => f.FixAvailable && !f.Id.StartsWith("security.usb.", StringComparison.Ordinal)) ?? []);
            Add("optimize", optimize.Result.Where(s => s.Available && !s.Applied && s.Tweak.Profiles.Contains(Profile)).Select(TweakFixer.FindingFor));
            Add("bloatware", apps.Result?.Apps.Where(a => a.IsBloat && a.CanUninstall).Select(UninstallFixer.FindingFor) ?? []);
            Add("windows", updates.Result?.Windows.Select(UpdateFixer.ForWindows) ?? []);
            Add("software", updates.Result?.Software.Select(UpdateFixer.ForUpgrade) ?? []);
            Add("cleanup", cleanup.Result?.Categories.Where(c => c.Bytes > 0).Select(c => CleanupFixer.FindingFor(c.Id, c.Risk, c.Bytes, c.Files)) ?? []);
            _loaded = true;
            Status = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Lance toutes les étapes cochées, dans l'ordre, en une seule série (un seul point de restauration).</summary>
    [RelayCommand]
    private void Run()
    {
        var findings = new List<Finding>();
        foreach (var step in Steps.Where(s => s.IsSelected))
        {
            findings.AddRange(step.Findings);
            if (step.Id == "software")
            {
                // Les installations viennent après les mises à jour des logiciels déjà présents, avant le nettoyage.
                findings.AddRange(Software.Where(s => s.IsSelected).Select(s => UpdateFixer.ForInstall(s.Package)));
            }
        }

        if (!Steps.Any(s => s.Id == "software" && s.IsSelected))
        {
            var cleanupIndex = findings.FindIndex(f => f.Module == "cleanup");
            var installs = Software.Where(s => s.IsSelected).Select(s => UpdateFixer.ForInstall(s.Package)).ToList();
            findings.InsertRange(cleanupIndex < 0 ? findings.Count : cleanupIndex, installs);
        }

        if (findings.Count > 0)
        {
            Fixes?.Request(findings);
        }
    }

    private void Add(string id, IEnumerable<Finding> findings)
    {
        var step = new SetupStep(id, _localizer[$"Setup.{id}.Title"], _localizer[$"Setup.{id}.Explanation"]);
        step.Findings.AddRange(findings.Where(f => FixPolicy.IsOffered(f, _mode())));
        step.Summary = step.Findings.Count == 0
            ? _localizer["Setup.NothingToDo"]
            : _localizer.Format(new LocalizedText("Setup.Count", step.Findings.Count));
        step.IsSelected = step.Findings.Count > 0;
        Steps.Add(step);
    }

    /// <summary>Une analyse qui échoue (module indisponible) n'empêche pas les autres étapes.</summary>
    private static async Task<T> Safe<T>(Func<Task<T>> run, T fallback)
    {
        try
        {
            return await run().ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return fallback;
        }
    }
}
