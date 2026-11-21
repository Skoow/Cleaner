using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Boot;
using PCDoc.Modules.System.Inventory;
using PCDoc.Modules.System.Advisor;
using PCDoc.Modules.System.Fixes;
using PCDoc.Core.Settings;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.SystemPage;

/// <summary>
/// Page Système (lecture seule) : trois sources lues en parallèle ; une source en panne n'affecte que sa section.
/// Tous les textes sont reconstruits quand la langue change.
/// </summary>
public sealed partial class SystemPageViewModel : ObservableObject
{
    private readonly IInventorySource _inventorySource;
    private readonly IBootSource _bootSource;
    private readonly IAutorunSource _autorunSource;
    private readonly IClipboardService? _clipboard;
    private IReadOnlyList<AutorunEntry> _allEntries = [];
    private int _loading;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private SystemInventory? _inventory;

    [ObservableProperty]
    private BootHistory? _boot;

    [ObservableProperty]
    private bool _inventoryUnavailable;

    [ObservableProperty]
    private bool _bootUnavailable;

    [ObservableProperty]
    private bool _autorunsUnavailable;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AutorunKind? _kindFilter = AutorunKind.Startup;

    [ObservableProperty]
    private bool _unsignedOnly;

    [ObservableProperty]
    private IReadOnlyList<InfoCard> _overviewCards = [];

    [ObservableProperty]
    private IReadOnlyList<InfoCard> _hardwareCards = [];

    [ObservableProperty]
    private IReadOnlyList<DiskRow> _disks = [];

    [ObservableProperty]
    private IReadOnlyList<VolumeRow> _volumes = [];

    [ObservableProperty]
    private IReadOnlyList<BootBar> _bootBars = [];

    [ObservableProperty]
    private IReadOnlyList<DegradationRow> _lastDegradations = [];

    [ObservableProperty]
    private string _lastBootText = "—";

    [ObservableProperty]
    private string _medianText = "—";

    [ObservableProperty]
    private string _trendText = string.Empty;

    [ObservableProperty]
    private bool _trendIsUp;

    [ObservableProperty]
    private string _bootChartDescription = string.Empty;

    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>Onglet affiché : 0 Vue d'ensemble … 4 Démarrage, 5 Tâches, 6 Services.</summary>
    [ObservableProperty]
    private int _selectedTab;

    private readonly AutorunAdvisor? _advisor;
    private readonly Func<AppMode> _mode;

    public SystemPageViewModel(
        IInventorySource inventory,
        IBootSource boot,
        IAutorunSource autoruns,
        ILocalizer localizer,
        IClipboardService? clipboard = null,
        AutorunAdvisor? advisor = null,
        FixConfirmationViewModel? fixes = null,
        Func<AppMode>? mode = null)
    {
        _clipboard = clipboard;
        _advisor = advisor;
        _mode = mode ?? (() => AppMode.Novice);
        Fixes = fixes;
        if (fixes is not null)
        {
            fixes.Completed += (_, _) => _ = RefreshAsync();
        }

        _inventorySource = inventory;
        _bootSource = boot;
        _autorunSource = autoruns;
        Localizer = localizer;
        localizer.PropertyChanged += (_, _) => Rebuild();
    }

    public ILocalizer Localizer { get; }

    /// <summary>Panneau de confirmation des activations/désactivations (null si indisponible).</summary>
    public FixConfirmationViewModel? Fixes { get; }

    public ObservableCollection<AutorunRow> Autoruns { get; } = [];

    public bool HasBootData => Boot is { Boots.Count: > 0 };

    public bool HasLoaded { get; private set; }

    /// <summary>Appelé à la première ouverture de la page.</summary>
    public Task EnsureLoadedAsync() => HasLoaded ? Task.CompletedTask : RefreshAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _loading, 1) == 1)
        {
            return; // une actualisation est déjà en cours
        }

        IsLoading = true;
        try
        {
            var inventoryTask = Task.Run(_inventorySource.Collect);
            var bootTask = Task.Run(() => _bootSource.Read());
            var autorunTask = Task.Run(_autorunSource.CollectAll);

            (Inventory, InventoryUnavailable) = await Read(inventoryTask).ConfigureAwait(true);
            (Boot, BootUnavailable) = await Read(bootTask).ConfigureAwait(true);
            var (collection, autorunsFailed) = await Read(autorunTask).ConfigureAwait(true);
            _allEntries = collection?.Entries ?? [];
            AutorunsUnavailable = autorunsFailed || collection is { Entries.Count: 0, FailedSources.Count: > 0 };
            HasLoaded = true;
            Rebuild();
        }
        finally
        {
            IsLoading = false;
            Interlocked.Exchange(ref _loading, 0);
        }
    }

    private static async Task<(T? Value, bool Failed)> Read<T>(Task<T> task)
        where T : class
    {
        try
        {
            return (await task.ConfigureAwait(true), false);
        }
#pragma warning disable CA1031 // Une source en panne = section « Non disponible », jamais un plantage.
        catch (Exception)
#pragma warning restore CA1031
        {
            return (null, true);
        }
    }

    partial void OnSelectedTabChanged(int value)
    {
        var kind = value switch
        {
            4 => AutorunKind.Startup,
            5 => AutorunKind.ScheduledTask,
            6 => AutorunKind.Service,
            _ => (AutorunKind?)null,
        };
        if (kind is not null)
        {
            KindFilter = kind;
        }
    }

    [RelayCommand]
    private void Toggle(AutorunRow? row)
    {
        if (row?.ToggleFinding is { } finding)
        {
            Fixes?.Request([finding]);
        }
    }

    [RelayCommand]
    private void CopyDetails(AutorunRow? row)
    {
        if (row is not null)
        {
            _clipboard?.SetText($"{row.Name}\n{row.Details}");
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnKindFilterChanged(AutorunKind? value) => ApplyFilter();

    partial void OnUnsignedOnlyChanged(bool value) => ApplyFilter();

    partial void OnBootChanged(BootHistory? value) => OnPropertyChanged(nameof(HasBootData));

    private string T(string key, params object[] args) => Localizer.Format(new LocalizedText(key, args));

    private void Rebuild()
    {
        BuildCards();
        BuildBoot();
        ApplyFilter();
    }

    // ---------- Vue d'ensemble et Matériel ----------
    private void BuildCards()
    {
        var pc = Inventory;
        if (pc is null)
        {
            OverviewCards = HardwareCards = [];
            Disks = [];
            Volumes = [];
            return;
        }

        var overview = new List<InfoCard>
        {
            Card(pc.Windows, T("Card.Windows"), "", w => new InfoCard(T("Card.Windows"), "",
            [
                new(T("Label.Edition"), w.Edition),
                new(T("Label.Version"), T("Value.Version", w.DisplayVersion ?? w.Name, w.Build, w.Ubr)),
                new(T("Label.InstalledOn"), w.InstallDate?.ToString("d", Localizer is Localizer l ? l.Culture : null) ?? T("Value.Unknown")),
                new(T("Label.Activation"), w.Activation switch
                {
                    ActivationState.Activated => T("Value.Activated"),
                    ActivationState.NotActivated => T("Value.NotActivated"),
                    _ => T("Value.Unknown"),
                }),
            ])),
            Card(pc.Cpu, T("Card.Cpu"), "", c => new InfoCard(T("Card.Cpu"), "",
            [
                new(T("Label.Model"), c.Name),
                new(T("Label.CoresThreads"), T("Value.CoresThreads", c.Cores, c.LogicalProcessors)),
                new(T("Label.Frequency"), T("Value.GHz", c.MaxClockMHz / 1000.0)),
            ])),
            Card(pc.Memory, T("Card.Memory"), "", m => new InfoCard(T("Card.Memory"), "",
                [new(T("Label.Modules"), m.Modules.Count == 0 ? T("Value.Unknown") : $"{m.Modules.Count} × {T("Value.GB", m.Modules[0].CapacityBytes / 1e9)}")],
                Gauge: m.TotalBytes > 0 ? (double)m.UsedBytes / m.TotalBytes : null,
                Note: T("Value.MemoryUsed", m.UsedBytes / 1e9, m.TotalBytes / 1e9))),
        };

        if (pc.Gpus.Value is { Count: > 0 } gpus)
        {
            overview.Add(new InfoCard(T("Card.Gpu"), "",
            [
                new(T("Label.Model"), gpus[0].Name),
                new(T("Label.VideoMemory"), gpus[0].MemoryBytes is { } mem ? T("Value.GB", mem / 1e9) : T("Value.Unknown")),
                new(T("Label.Driver"), gpus[0].DriverVersion ?? T("Value.Unknown")),
            ]));
        }

        if (pc.Volumes.Value?.FirstOrDefault(v => v.Letter.Equals("C:", StringComparison.OrdinalIgnoreCase)) is { } c)
        {
            var systemDisk = pc.Disks.Value is { Count: > 0 } diskList ? diskList[0] : null;
            overview.Add(new InfoCard(T("Card.Disk", c.Letter), "",
                systemDisk is null ? [] : [new(T("Label.Type"), DiskType(systemDisk))],
                Gauge: c.SizeBytes > 0 ? 1 - c.FreeRatio : null, GaugeWarning: c.FreeRatio < 0.10,
                Note: T("Value.FreeOf", c.FreeBytes / 1e9, c.SizeBytes / 1e9)));
        }

        overview.Add(pc.Batteries.Available
            ? pc.Batteries.Value is { Count: > 0 } batteries
                ? new InfoCard(T("Card.Battery"), "", [.. batteries.Select(b => new InfoLine(T("Label.Wear"), T("Value.Percent", b.Wear * 100)))])
                : new InfoCard(T("Card.Battery"), "", [], Note: T("System.NoBattery"))
            : Unavailable(T("Card.Battery"), "", pc.Batteries.Reason));

        overview.Add(Card(pc.Win11, T("Card.Win11"), "", r => new InfoCard(T("Card.Win11"), "",
            [.. r.Checks.Select(chk => new InfoLine(T($"Win11.{chk.Check}"), T($"Check.{chk.State}"), chk.State))])));
        OverviewCards = overview;

        HardwareCards =
        [
            new InfoCard(T("Card.Board"), "",
            [
                new(T("Label.Board"), Join(pc.Board.Value?.Manufacturer, pc.Board.Value?.Product)),
                new(T("Label.Bios"), Join(pc.Bios.Value?.Manufacturer, pc.Bios.Value?.Version)),
                new(T("Label.BiosDate"), pc.Bios.Value?.ReleaseDate?.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? T("Value.Unknown")),
            ]),
            Card(pc.Memory, T("Card.Modules"), "", m => new InfoCard(T("Card.Modules"), "",
                [.. m.Modules.Select(x => new InfoLine(x.Locator ?? "—", T("Value.ModuleLine", x.CapacityBytes / 1e9, x.SpeedMHz, x.Manufacturer ?? "—")))])),
            Card(pc.Displays, T("Card.Displays"), "", d => new InfoCard(T("Card.Displays"), "",
                [.. d.Select(x => new InfoLine(x.Name ?? T("Value.NoName"), x.DiagonalInches is { } inch ? T("Value.Inches", inch) : "—"))])),
        ];

        Disks = [.. pc.Disks.Value?.Select(d => new DiskRow(d.Name, DiskType(d), Size(d.SizeBytes))) ?? []];
        Volumes = [.. pc.Volumes.Value?.Select(v => new VolumeRow(v.Letter, v.FileSystem ?? "—", Size(v.SizeBytes),
            $"{Size(v.FreeBytes)} ({T("Value.Percent", v.FreeRatio * 100)})", v.FreeRatio < 0.10)) ?? []];
    }

    private InfoCard Card<TValue>(Section<TValue> section, string title, string glyph, Func<TValue, InfoCard> build) =>
        section is { Available: true, Value: { } value } ? build(value) : Unavailable(title, glyph, section.Reason);

    private InfoCard Unavailable(string title, string glyph, string? reason) =>
        new(title, glyph, [], Note: T("System.Unavailable", T($"Reason.{reason ?? "error"}")));

    private string DiskType(DiskInfo d) => d.MediaType switch
    {
        DiskMediaType.Ssd or DiskMediaType.Scm => d.IsNvme ? T("Value.SsdNvme") : T("Value.Ssd"),
        DiskMediaType.Hdd => T("Value.Hdd"),
        _ => T("Value.Unknown"),
    };

    private string Size(long bytes) => bytes >= 1e12 ? T("Value.TB", bytes / 1e12) : T("Value.GB", bytes / 1e9);

    private static string Join(string? a, string? b) => string.Join(' ', new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } s ? s : "—";

    // ---------- Démarrage ----------
    private void BuildBoot()
    {
        OnPropertyChanged(nameof(HasBootData));
        var history = Boot;
        if (history is not { Boots.Count: > 0 })
        {
            BootBars = [];
            LastDegradations = [];
            LastBootText = MedianText = "—";
            TrendText = BootChartDescription = string.Empty;
            return;
        }

        var chronological = history.Boots.Reverse().ToList(); // le plus récent à droite
        var max = chronological.Max(b => b.Total.TotalSeconds);
        BootBars = [.. chronological.Select((b, i) => new BootBar(
            max <= 0 ? 0 : b.Total.TotalSeconds / max, i == chronological.Count - 1, T("Boot.Seconds", b.Total.TotalSeconds)))];

        var median = history.Median(5)!.Value.TotalSeconds;
        LastBootText = T("Boot.Seconds", history.Boots[0].Total.TotalSeconds);
        MedianText = T("Boot.Seconds", median);
        TrendText = T($"Trend.{history.Trend}");
        TrendIsUp = history.Trend == BootTrend.Up;
        BootChartDescription = T("Boot.ChartAlt", history.Boots.Count, median, TrendText);
        LastDegradations = [.. history.Boots[0].Degradations
            .OrderByDescending(d => d.DegradationTime)
            .Select(d => new DegradationRow(d.FriendlyName, T($"Degradation.{d.Kind}"), T("Boot.SecondsPrecise", d.DegradationTime.TotalSeconds)))];
    }

    // ---------- Listes ----------
    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var rows = _allEntries
            .Where(e => KindFilter is null
                        || e.Kind == KindFilter
                        || (KindFilter == AutorunKind.Service && e.Kind == AutorunKind.Driver))
            .Select(ToRow)
            .Where(r => !UnsignedOnly || r.IsUnsigned)
            .Where(r => search.Length == 0
                        || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || r.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Autoruns.Clear();
        foreach (var row in rows)
        {
            Autoruns.Add(row);
        }

        CountText = T("List.Count", rows.Count);
    }

    private AutorunRow ToRow(AutorunEntry e)
    {
        var badge = e.Signature.Status switch
        {
            SignatureStatus.Signed => SignatureBadge.Signed,
            SignatureStatus.Unsigned or SignatureStatus.Invalid => SignatureBadge.Unsigned,
            SignatureStatus.FileNotFound => SignatureBadge.Missing,
            _ => SignatureBadge.None,
        };
        var label = badge switch
        {
            SignatureBadge.Signed => T("Sig.Signed"),
            SignatureBadge.Unsigned => T("Sig.Unsigned"),
            SignatureBadge.Missing => T("Sig.Missing"),
            _ => T("Sig.NotApplicable"),
        };
        var row = new AutorunRow(e, e.Name, e.Signature.Publisher ?? "—", T($"Kind.{e.Kind}"), e.Location, e.CommandLine,
            badge, label, e.Enabled ? T("State.Enabled") : T("State.Disabled"));
        if (_advisor is null)
        {
            return row;
        }

        var advice = _advisor.Advise(e);
        Finding? toggle = null;
        if (Fixes is not null && advice.Level != RiskLevel.Locked && ToggleRequest.For(e, advice.Action) is { } request)
        {
            var display = e.Kind == AutorunKind.ScheduledTask ? e.Name.Split('\\')[^1] : e.Name;
            toggle = new Finding(
                $"system.toggle.{e.Kind}.{e.ServiceName ?? e.Name}".ToLowerInvariant(), "system", Severity.Info,
                new LocalizedText(advice.Action == ToggleAction.Disable ? "Toggle.Disable.Title" : "Toggle.Enable.Title", display),
                new LocalizedText(advice.ReasonKey), $"{e.Location}\n{e.CommandLine}", true, advice.Level, FixData: request.Serialize());
        }

        return row with
        {
            AdviceLabel = T($"Risk.{advice.Level}"),
            AdviceLevel = advice.Level,
            AdviceReason = T(advice.ReasonKey),
            ToggleLabel = T(advice.Action == ToggleAction.Disable ? "Toggle.Disable" : "Toggle.Enable"),
            ToggleFinding = toggle,
            CanToggle = toggle is not null && FixPolicy.IsOffered(toggle, _mode()),
        };
    }
}
