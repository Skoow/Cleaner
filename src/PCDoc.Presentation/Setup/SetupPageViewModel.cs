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

/// <summary>Mesures avant/après une préparation.</summary>
public sealed record SetupSnapshot(long FreeBytes, int Actions, int Apps, int WindowsUpdates, int SoftwareUpdates, long CleanableBytes);

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
    private SetupSnapshot? _current;
    private SetupSnapshot? _before;

    [ObservableProperty]
    private string? _comparison;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private TechProfile? _selectedTechProfile;

    private TechProfile? _pendingProfile;

    /// <summary>Dossier des profils de technicien (null = fonction désactivée).</summary>
    public TechProfileStore? TechProfileFolder { get; init; }

    public ObservableCollection<TechProfile> TechProfiles { get; } = [];

    /// <summary>Enregistre les choix actuels (étapes, usage, logiciels) comme profil réutilisable.</summary>
    [RelayCommand]
    private void SaveTechProfile()
    {
        if (TechProfileFolder is null || TechProfileStore.SafeName(ProfileName).Length == 0)
        {
            Status = _localizer["Setup.Profile.NeedName"];
            return;
        }

        try
        {
            var path = TechProfileFolder.Save(new TechProfile(ProfileName, Profile, [.. Steps.Where(s => s.IsSelected).Select(s => s.Id)],
                [.. Software.Where(s => s.IsSelected).Select(s => s.Package.Id)]));
            Status = _localizer.Format(new LocalizedText("Setup.Profile.Saved", path));
            RefreshTechProfiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = _localizer["Setup.Profile.SaveFailed"];
        }
    }

    partial void OnSelectedTechProfileChanged(TechProfile? value)
    {
        if (value is not null)
        {
            ApplyTechProfile(value);
        }
    }

    /// <summary>Applique un profil : usage, étapes et logiciels (identifiants inconnus ignorés).</summary>
    internal void ApplyTechProfile(TechProfile profile)
    {
        ProfileName = profile.Name;
        if (TweakCatalog.ProfileIds.Contains(profile.Usage))
        {
            Profile = profile.Usage;
        }

        foreach (var s in Software)
        {
            s.IsSelected = profile.Software.Contains(s.Package.Id, StringComparer.OrdinalIgnoreCase);
        }

        if (Steps.Count == 0)
        {
            _pendingProfile = profile; // appliqué à la fin de l'analyse
            return;
        }

        foreach (var step in Steps)
        {
            step.IsSelected = step.Findings.Count > 0 && profile.Steps.Contains(step.Id);
        }
    }

    private void RefreshTechProfiles()
    {
        TechProfiles.Clear();
        foreach (var p in TechProfileFolder?.List() ?? [])
        {
            TechProfiles.Add(p);
        }
    }

    /// <summary>Espace libre du disque de Windows (mesure avant/après).</summary>
    public Func<long>? FreeSpace { get; init; }

    /// <summary>Journal d'activité : la comparaison avant/après y est inscrite.</summary>
    public Core.Activity.IActivityLog? Activity { get; init; }

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
                if (_before is { } before && _current is { } after)
                {
                    Comparison = Compare(before, after);
                    foreach (var line in Comparison.Split('\n'))
                    {
                        Activity?.Write("setup", line);
                    }
                }
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

    public Task EnsureLoadedAsync()
    {
        if (TechProfiles.Count == 0)
        {
            RefreshTechProfiles();
        }

        return _loaded ? Task.CompletedTask : AnalyzeAsync();
    }

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
            // Ordre logique d'une préparation : faire de la place, retirer l'inutile, chercher les virus, protéger,
            // régler la confidentialité et les performances, puis tout mettre à jour.
            Add("cleanup", cleanup.Result?.Categories.Where(c => c.Bytes > 0).Select(c => CleanupFixer.FindingFor(c.Id, c.Risk, c.Bytes, c.Files)) ?? []);
            Add("bloatware", apps.Result?.Apps.Where(a => a.IsBloat && a.CanUninstall).Select(UninstallFixer.FindingFor) ?? []);
            Add("antivirus", [ScanFixer.FindingFor(ScanKind.DefenderQuick), ScanFixer.FindingFor(ScanKind.AdwCleaner)]);
            Add("security", security.Result?.Findings.Where(f => f.FixAvailable && !f.Id.StartsWith("security.usb.", StringComparison.Ordinal)) ?? []);
            Add("privacy", privacy.Result.Where(s => s.Available && !s.Applied && s.Tweak.Profiles.Contains("recommended")).Select(TweakFixer.FindingFor));
            _optimizeStates = optimize.Result;
            Add("optimize", OptimizeFindings());
            // Les définitions de l'antivirus passent par l'étape Sécurité (Defender) : pas deux fois.
            Add("windows", updates.Result?.Windows.Where(w => !IsDefenderDefinitions(w)).Select(UpdateFixer.ForWindows) ?? []);
            Add("software", updates.Result?.Software.Select(UpdateFixer.ForUpgrade) ?? []);
            _current = new SetupSnapshot(
                SafeFree(), Steps.Sum(s => s.Findings.Count), apps.Result?.Apps.Count ?? 0, updates.Result?.Windows.Count ?? 0,
                updates.Result?.Software.Count ?? 0, cleanup.Result?.Categories.Sum(c => c.Bytes) ?? 0);
            _loaded = true;
            Status = null;
            if (_pendingProfile is { } pending)
            {
                _pendingProfile = null;
                ApplyTechProfile(pending);
            }
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
                // Les installations viennent après les mises à jour des logiciels déjà présents.
                findings.AddRange(Software.Where(s => s.IsSelected).Select(s => UpdateFixer.ForInstall(s.Package)));
            }
        }

        if (!Steps.Any(s => s.Id == "software" && s.IsSelected))
        {
            findings.AddRange(Software.Where(s => s.IsSelected).Select(s => UpdateFixer.ForInstall(s.Package))); // en dernier
        }

        if (findings.Count > 0)
        {
            _before = _current;
            Comparison = null;
            Fixes?.Request(findings);
        }
    }

    private long SafeFree()
    {
        try
        {
            return FreeSpace?.Invoke() ?? 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>Texte avant → après, une ligne par mesure.</summary>
    internal string Compare(SetupSnapshot before, SetupSnapshot after)
    {
        string Gb(long b) => _localizer.Format(new LocalizedText("Size.GB", b / (double)(1L << 30)));
        return string.Join('\n',
            _localizer.Format(new LocalizedText("Setup.Compare.Title")),
            _localizer.Format(new LocalizedText("Setup.Compare.Free", Gb(before.FreeBytes), Gb(after.FreeBytes))),
            _localizer.Format(new LocalizedText("Setup.Compare.Actions", before.Actions, after.Actions)),
            _localizer.Format(new LocalizedText("Setup.Compare.Apps", before.Apps, after.Apps)),
            _localizer.Format(new LocalizedText("Setup.Compare.Windows", before.WindowsUpdates, after.WindowsUpdates)),
            _localizer.Format(new LocalizedText("Setup.Compare.Software", before.SoftwareUpdates, after.SoftwareUpdates)),
            _localizer.Format(new LocalizedText("Setup.Compare.Cleanable", Gb(before.CleanableBytes), Gb(after.CleanableBytes))));
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
            // Hors du fil de l'interface : certaines analyses commencent par des requêtes WMI bloquantes.
            return await Task.Run(run).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return fallback;
        }
    }
}
