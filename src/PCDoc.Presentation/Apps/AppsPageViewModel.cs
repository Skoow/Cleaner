using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Apps;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Apps;

/// <summary>Une application de la liste.</summary>
public sealed partial class AppRow(AppItem item, Finding finding, bool canUninstall, string kind, string size, string date, string riskLabel, string reason)
    : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public AppItem Item { get; } = item;

    public string Name => Item.App.Name;

    public string Publisher => Item.App.Publisher ?? string.Empty;

    public string Version => Item.App.Version ?? string.Empty;

    public string Kind { get; } = kind;

    public string Size { get; } = size;

    public string Date { get; } = date;

    public string RiskLabel { get; } = riskLabel;

    public string Reason { get; } = reason;

    public Finding Finding { get; } = finding;

    /// <summary>Faux si verrouillé, non pris en charge ou non proposé dans le mode actuel.</summary>
    public bool CanUninstall { get; } = canUninstall;
}

public sealed record LeftoverRow(string Path, Finding Finding);

/// <summary>Page Applications : liste avec recherche, applications préinstallées à cocher, restes en quarantaine.</summary>
public sealed partial class AppsPageViewModel : ObservableObject
{
    private readonly IAppsSource _source;
    private readonly ILocalizer _localizer;
    private readonly Func<Core.Settings.AppMode> _mode;
    private AppsReport? _report;

    // Sélection des applications préinstallées : initialisée une seule fois (🟢 cochées), puis gardée telle quelle.
    private HashSet<string>? _selected;
    private bool _analyzeAgain;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _countText = string.Empty;

    public AppsPageViewModel(IAppsSource source, ILocalizer localizer, FixConfirmationViewModel? fixes, Func<Core.Settings.AppMode>? mode = null)
    {
        _source = source;
        _localizer = localizer;
        _mode = mode ?? (() => Core.Settings.AppMode.Novice);
        Fixes = fixes;
        if (fixes is not null)
        {
            fixes.Completed += (_, _) => _ = AnalyzeAsync();
        }

        localizer.PropertyChanged += (_, _) => Build();
    }

    public FixConfirmationViewModel? Fixes { get; }

    public ObservableCollection<AppRow> Installed { get; } = [];

    public ObservableCollection<AppRow> Preinstalled { get; } = [];

    public ObservableCollection<LeftoverRow> Leftovers { get; } = [];

    public bool HasLeftovers => Leftovers.Count > 0;

    public bool HasLoaded => _report is not null;

    public Task EnsureLoadedAsync() => HasLoaded ? Task.CompletedTask : AnalyzeAsync();

    partial void OnSearchTextChanged(string value) => Build();

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsLoading)
        {
            _analyzeAgain = true; // analyse déjà en cours : une seule, relancée à la fin si besoin
            return;
        }

        IsLoading = true;
        try
        {
            do
            {
                _analyzeAgain = false;
                _report = await Task.Run(() => _source.AnalyzeAsync(CancellationToken.None)).ConfigureAwait(true);
                Build();
            }
            while (_analyzeAgain);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Uninstall(AppRow? row)
    {
        if (row is { CanUninstall: true })
        {
            Fixes?.Request([row.Finding]);
        }
    }

    [RelayCommand]
    private void RemoveSelected() => Fixes?.Request(Preinstalled.Where(r => r.IsSelected && r.CanUninstall).Select(r => r.Finding));

    [RelayCommand]
    private void QuarantineLeftovers() => Fixes?.Request(Leftovers.Select(l => l.Finding));

    private void Build()
    {
        if (_report is not { } report)
        {
            return;
        }

        _selected ??= [.. report.Apps.Where(i => i.IsBloat && i.Risk == RiskLevel.Safe).Select(i => i.App.Id)];
        Installed.Clear();
        Preinstalled.Clear();
        var search = SearchText.Trim();
        foreach (var item in report.Apps)
        {
            var row = RowFor(item);
            if (search.Length == 0 || row.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Publisher.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            {
                Installed.Add(row);
            }

            if (item.IsBloat)
            {
                var bloat = RowFor(item);
                var selected = _selected;
                bloat.IsSelected = bloat.CanUninstall && selected.Contains(item.App.Id);
                bloat.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(AppRow.IsSelected))
                    {
                        return;
                    }

                    if (bloat.IsSelected)
                    {
                        selected.Add(bloat.Item.App.Id);
                    }
                    else
                    {
                        selected.Remove(bloat.Item.App.Id);
                    }
                };
                Preinstalled.Add(bloat);
            }
        }

        Leftovers.Clear();
        foreach (var folder in report.Leftovers)
        {
            Leftovers.Add(new LeftoverRow(folder, LeftoversFixer.FindingFor(folder)));
        }

        CountText = _localizer.Format(new LocalizedText("Apps.Count", report.Apps.Count));
        OnPropertyChanged(nameof(HasLeftovers));
        OnPropertyChanged(nameof(HasLoaded));
    }

    private AppRow RowFor(AppItem item)
    {
        var finding = UninstallFixer.FindingFor(item);
        var app = item.App;
        var kind = app.Kind == AppKind.Store ? _localizer["Apps.Kind.Store"] : _localizer["Apps.Kind.Win32"];
        var size = app.SizeBytes <= 0 ? string.Empty
            : app.SizeBytes >= 1L << 30 ? _localizer.Format(new LocalizedText("Size.GB", app.SizeBytes / (double)(1L << 30)))
            : _localizer.Format(new LocalizedText("Size.MB", app.SizeBytes / (double)(1L << 20)));
        var date = app.InstallDate?.ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;
        var reason = item.Entry is { } e ? _localizer[$"Apps.Reason.{e.Reason}"]
            : item.Plan is null ? _localizer["Apps.NotSupported"] : string.Empty;
        return new AppRow(item, finding, FixPolicy.IsOffered(finding, _mode()), kind, size, date, _localizer[$"Risk.{item.Risk}"], reason);
    }
}
