using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Security;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Security;

public sealed partial class AuditRow(Finding finding, string title, string explanation, string severityLabel, string riskLabel, bool canSelect)
    : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public Finding Finding { get; } = finding;

    public string Title { get; } = title;

    public string Explanation { get; } = explanation;

    public string Details => Finding.TechnicalDetails;

    public string SeverityLabel { get; } = severityLabel;

    public string RiskLabel { get; } = riskLabel;

    public bool CanSelect { get; } = canSelect;
}

public sealed record AntivirusRow(string Name, string Status);

/// <summary>Analyse antivirus à cocher (Defender, Safety Scanner, AdwCleaner, Kaspersky).</summary>
public sealed partial class ScanRow(ScanKind kind, string title, string explanation, string riskLabel, bool canSelect) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ScanKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string Explanation { get; } = explanation;

    public string RiskLabel { get; } = riskLabel;

    public bool CanSelect { get; } = canSelect;
}

public sealed record DetectionRow(string Name, string When, string Status, string Resource);

public sealed record UsbRow(string Root, string Title, string Status, Finding? Finding, bool CanFix);

/// <summary>Page Sécurité : protection (antivirus, analyses), audit (corrections), clés USB.</summary>
#pragma warning disable CA1001 // _scan est libéré par le « using » de ScanAsync ; le champ sert seulement à l'annulation
public sealed partial class SecurityPageViewModel : ObservableObject
{
    private readonly ISecuritySource _source;
    private readonly ILocalizer _localizer;
    private readonly Func<Core.Settings.AppMode> _mode;
    private SecurityReport? _report;
    private HashSet<string>? _selected;
    private bool _analyzeAgain;
    private CancellationTokenSource? _scan;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuickScanCommand), nameof(FullScanCommand), nameof(ScanDriveCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string? _scanStatus;

    [ObservableProperty]
    private string? _scanLine;

    [ObservableProperty]
    private string? _auditStatus;

    [ObservableProperty]
    private string? _usbStatus;

    public SecurityPageViewModel(ISecuritySource source, ILocalizer localizer, FixConfirmationViewModel? fixes, Func<Core.Settings.AppMode>? mode = null)
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

    /// <summary>Activité réseau, extensions, seconde analyse et VirusTotal (null = indisponible).</summary>
    public SecurityToolsViewModel? Tools { get; init; }

    public ObservableCollection<AntivirusRow> Antivirus { get; } = [];

    public ObservableCollection<DetectionRow> Detections { get; } = [];

    public ObservableCollection<AuditRow> Audit { get; } = [];

    public ObservableCollection<UsbRow> Usb { get; } = [];

    public ObservableCollection<ScanRow> Scans { get; } = [];

    /// <summary>Analyses lancées par le pipeline (leur progression s'affiche dans la page).</summary>
    public ScanFixer? ScanTools
    {
        init
        {
            if (value is not null)
            {
                value.Output += (_, line) => ScanLine = line;
            }
        }
    }

    /// <summary>Les analyses cochées s'enchaînent dans une seule série (un point de restauration avant celles qui suppriment).</summary>
    [RelayCommand]
    private void RunSelectedScans() => Fixes?.Request(Scans.Where(s => s.IsSelected && s.CanSelect).Select(s => ScanFixer.FindingFor(s.Kind)));

    private void BuildScans()
    {
        if (Scans.Count > 0)
        {
            return;
        }

        foreach (var kind in Enum.GetValues<ScanKind>())
        {
            var finding = ScanFixer.FindingFor(kind);
            var canSelect = FixPolicy.IsOffered(finding, _mode());
            Scans.Add(new ScanRow(kind, _localizer[$"Scan.{kind}.Title"], _localizer[$"Scan.{kind}.Explanation"], _localizer[$"Risk.{finding.Risk}"], canSelect)
            {
                IsSelected = canSelect && kind is ScanKind.DefenderQuick or ScanKind.AdwCleaner,
            });
        }
    }

    public bool HasLoaded => _report is not null;

    public Task EnsureLoadedAsync() => HasLoaded ? Task.CompletedTask : AnalyzeAsync();

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsLoading)
        {
            _analyzeAgain = true; // une correction vient de finir pendant l'analyse : on relira l'état ensuite
            return;
        }

        IsLoading = true;
        try
        {
            do
            {
                _analyzeAgain = false;
                _report = await Task.Run(() => _source.AnalyzeAsync(CancellationToken.None)).ConfigureAwait(true);
                _selected = null;
                Build();
            }
            while (_analyzeAgain);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanScan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task QuickScanAsync() => ScanAsync(1, null);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task FullScanAsync() => ScanAsync(2, null);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task ScanDriveAsync(UsbRow? row) => row is null ? Task.CompletedTask : ScanAsync(3, row.Root);

    /// <summary>Arrête l'analyse Defender en cours (une analyse complète peut durer des heures).</summary>
    [RelayCommand]
    private void CancelScan() => _scan?.Cancel();

    [RelayCommand]
    private void FixSelected() => Fixes?.Request(Audit.Where(r => r.IsSelected && r.CanSelect).Select(r => r.Finding));

    [RelayCommand]
    private void FixUsb(UsbRow? row)
    {
        if (row is { CanFix: true, Finding: { } finding })
        {
            Fixes?.Request([finding]);
        }
    }

    private async Task ScanAsync(int type, string? root)
    {
        IsScanning = true;
        ScanStatus = _localizer["Security.Scanning"];
        using var scan = _scan = new CancellationTokenSource();
        try
        {
            var exit = await _source.ScanAsync(type, root, line => ScanLine = line, scan.Token).ConfigureAwait(true);
            ScanStatus = exit switch
            {
                0 => _localizer["Security.ScanClean"],
                2 => _localizer["Security.ScanThreats"],
                _ => _localizer["Security.ScanFailed"],
            };
        }
        catch (OperationCanceledException)
        {
            ScanStatus = _localizer["Security.ScanCancelled"];
        }
#pragma warning disable CA1031 // Defender absent, coupé ou remplacé : message à l'utilisateur, jamais de plantage
        catch (Exception)
#pragma warning restore CA1031
        {
            ScanStatus = _localizer["Security.ScanFailed"];
        }
        finally
        {
            _scan = null;
            IsScanning = false;
            ScanLine = null;
        }

        await AnalyzeAsync().ConfigureAwait(true);
    }

    private void Build()
    {
        if (_report is not { } report)
        {
            return;
        }

        BuildScans();
        Antivirus.Clear();
        foreach (var av in report.Antivirus)
        {
            Antivirus.Add(new AntivirusRow(av.Name,
                (av.Enabled ? _localizer["Security.Enabled"] : _localizer["Security.Disabled"]) + " · "
                + (av.UpToDate ? _localizer["Security.UpToDate"] : _localizer["Security.OutOfDate"])));
        }

        Detections.Clear();
        foreach (var d in report.Detections)
        {
            Detections.Add(new DetectionRow(d.Name, d.When ?? string.Empty,
                d.Removed ? _localizer["Security.Removed"] : _localizer["Security.NotRemoved"], d.Resource ?? string.Empty));
        }

        _selected ??= [.. report.Findings.Where(f => FixPolicy.IsInFixAll(f) && f.Severity != Severity.Info && !f.Id.StartsWith("security.usb.", StringComparison.Ordinal))
            .Select(f => f.Id)];
        var selected = _selected;
        Audit.Clear();
        foreach (var f in report.Findings.Where(f => !f.Id.StartsWith("security.usb.", StringComparison.Ordinal)).OrderByDescending(f => f.Severity))
        {
            var canSelect = FixPolicy.IsOffered(f, _mode());
            var row = new AuditRow(f, _localizer.Format(f.Title), _localizer.Format(f.Explanation), _localizer[$"Severity.{f.Severity}"],
                f.FixAvailable ? _localizer[$"Risk.{f.Risk}"] : string.Empty, canSelect)
            {
                IsSelected = canSelect && selected.Contains(f.Id),
            };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AuditRow.IsSelected))
                {
                    if (row.IsSelected)
                    {
                        selected.Add(row.Finding.Id);
                    }
                    else
                    {
                        selected.Remove(row.Finding.Id);
                    }
                }
            };
            Audit.Add(row);
        }

        Usb.Clear();
        foreach (var drive in report.Usb)
        {
            var finding = report.Findings.FirstOrDefault(f => f.Id == $"security.usb.{drive.Drive.Root[0]}");
            Usb.Add(new UsbRow(drive.Drive.Root, $"{drive.Drive.Root} {drive.Drive.Label}".Trim(),
                finding is null ? _localizer["Security.UsbClean"] : _localizer.Format(finding.Explanation), finding,
                finding is not null && FixPolicy.IsOffered(finding, _mode())));
        }

        AuditStatus = Audit.Count == 0 ? _localizer["Security.NoProblem"] : null;
        UsbStatus = Usb.Count == 0 ? _localizer["Security.NoUsb"] : null;
        OnPropertyChanged(nameof(HasLoaded));
    }
}
#pragma warning restore CA1001
