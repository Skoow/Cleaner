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
/// incluses, et seulement les 🟢 (même en mode expert : une préparation automatique ne fait que ce qui est sûr).
/// </summary>
public sealed partial class SetupPageViewModel : ObservableObject
{
    private readonly SetupSources _sources;
    private readonly ILocalizer _localizer;
    private readonly Func<Core.Settings.AppMode> _mode;
    private bool _loaded;
    private IReadOnlyList<TweakState> _optimizeStates = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(AnalyzeCommand))]
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
            fixes.Completed += async (_, _) =>
            {
                await AnalyzeAsync().ConfigureAwait(true); // état réel après la série (rien n'est redemandé deux fois)
                Status = _localizer["Setup.Done"];
            };
            fixes.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FixConfirmationViewModel.IsRunning))
                {
                    RunCommand.NotifyCanExecuteChanged();
                    AnalyzeCommand.NotifyCanExecuteChanged();
                }
            };
        }

        Software = [.. sources.Updates.Catalog.Select(p => new SetupSoftware(p, localizer[$"Updates.Category.{p.Category}"]))];
    }

    public FixConfirmationViewModel? Fixes { get; }

    public ObservableCollection<SetupStep> Steps { get; } = [];

    public IReadOnlyList<SetupSoftware> Software { get; }

    public IReadOnlyList<Optimize.ProfileChoice> Profiles =>
        [.. TweakCatalog.ProfileIds.Select(p => new Optimize.ProfileChoice(p, _localizer[$"Optimize.Profile.{p}"]))];

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : AnalyzeAsync();

    /// <summary>Changer de profil ne relance pas toutes les analyses : seule l'étape d'optimisation est recalculée.</summary>
    partial void OnProfileChanged(string value)
    {
        var index = Steps.ToList().FindIndex(s => s.Id == "optimize");
        if (index < 0)
        {
            return;
        }

        Steps.RemoveAt(index);
        Steps.Insert(index, MakeStep("optimize", OptimizeFindings()));
    }

    private bool IsBusy => IsLoading || Fixes?.IsRunning == true;

    private bool CanStart() => !IsBusy;

    private IEnumerable<Finding> OptimizeFindings() =>
        _optimizeStates.Where(s => s.Available && !s.Applied && s.Tweak.Profiles.Contains(Profile)).Select(TweakFixer.FindingFor);

    [RelayCommand]
    private void SelectProfile(string? profile)
    {
        if (profile is not null && TweakCatalog.ProfileIds.Contains(profile))
        {
            Profile = profile;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task AnalyzeAsync()
    {
        if (IsBusy)
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
            _optimizeStates = optimize.Result;
            Add("optimize", OptimizeFindings());
            Add("bloatware", apps.Result?.Apps.Where(a => a.IsBloat && a.CanUninstall).Select(UninstallFixer.FindingFor) ?? []);
            // Les définitions de l'antivirus passent par l'étape Sécurité (Defender) : pas deux fois.
            Add("windows", updates.Result?.Windows.Where(w => !IsDefenderDefinitions(w)).Select(UpdateFixer.ForWindows) ?? []);
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
    [RelayCommand(CanExecute = nameof(CanStart))]
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

    private static bool IsDefenderDefinitions(Core.Abstractions.WindowsUpdateItem update) =>
        update.KbArticle == "2267602" || update.Title.Contains("Security Intelligence", StringComparison.OrdinalIgnoreCase)
        || update.Title.Contains("Defender Antivirus", StringComparison.OrdinalIgnoreCase);

    private void Add(string id, IEnumerable<Finding> findings) => Steps.Add(MakeStep(id, findings));

    private SetupStep MakeStep(string id, IEnumerable<Finding> findings)
    {
        var step = new SetupStep(id, _localizer[$"Setup.{id}.Title"], _localizer[$"Setup.{id}.Explanation"]);
        step.Findings.AddRange(findings.Where(f => FixPolicy.IsOffered(f, _mode()) && FixPolicy.IsInFixAll(f)));
        step.Summary = step.Findings.Count == 0
            ? _localizer["Setup.NothingToDo"]
            : _localizer.Format(new LocalizedText("Setup.Count", step.Findings.Count));
        step.IsSelected = step.Findings.Count > 0;
        return step;
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
