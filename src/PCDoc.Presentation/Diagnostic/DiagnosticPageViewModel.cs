using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Diagnostic;
using PCDoc.Modules.Diagnostic.Repairs;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Diagnostic;

public sealed record DiagRow(string Date, string Name, string Detail, string Explanation);

public sealed record AppCrashRow(string App, int Count, string Module, string Last);

public sealed record DiskRow(string Name, string Health, string Wear, string Temperature);

public sealed record RepairRow(RepairKind Kind, string Title, string Explanation);

/// <summary>Page Diagnostic : écrans bleus, plantages, périphériques, disques, réparations, aide.</summary>
public sealed partial class DiagnosticPageViewModel : ObservableObject
{
    private readonly IDiagnosticSource _source;
    private readonly ILocalizer _localizer;
    private DiagnosticReport? _report;
    private int _loading;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _rebootPending;

    [ObservableProperty]
    private bool _disksUnavailable;

    [ObservableProperty]
    private bool _devicesUnavailable;

    [ObservableProperty]
    private string _shutdownsText = string.Empty;

    [ObservableProperty]
    private string _repairOutput = string.Empty;

    public DiagnosticPageViewModel(IDiagnosticSource source, ILocalizer localizer, RepairFixer? repairs = null, FixConfirmationViewModel? fixes = null)
    {
        _source = source;
        _localizer = localizer;
        Fixes = fixes;
        if (repairs is not null)
        {
            repairs.Output += (_, line) => RepairOutput = (RepairOutput.Length > 4000 ? RepairOutput[^3000..] : RepairOutput) + line + "\n";
        }

        localizer.PropertyChanged += (_, _) => Build();
    }

    public FixConfirmationViewModel? Fixes { get; }

    public ObservableCollection<DiagRow> BlueScreens { get; } = [];

    public ObservableCollection<AppCrashRow> AppCrashes { get; } = [];

    public ObservableCollection<DiagRow> Devices { get; } = [];

    public ObservableCollection<DiskRow> Disks { get; } = [];

    public ObservableCollection<DiagRow> OldDrivers { get; } = [];

    public ObservableCollection<RepairRow> Repairs { get; } = [];

    public bool HasLoaded => _report is not null;

    public Task EnsureLoadedAsync() => HasLoaded ? Task.CompletedTask : RefreshAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _loading, 1) == 1)
        {
            return;
        }

        IsLoading = true;
        try
        {
            _report = await Task.Run(_source.Collect).ConfigureAwait(true);
            Build();
        }
        finally
        {
            IsLoading = false;
            Interlocked.Exchange(ref _loading, 0);
        }
    }

    [RelayCommand]
    private void RunRepair(RepairKind kind)
    {
        RepairOutput = string.Empty;
        Fixes?.Request([RepairFixer.FindingFor(kind)]);
    }

    private string T(string key, params object[] args) => _localizer.Format(new LocalizedText(key, args));

    private string Date(DateTimeOffset d) => d.ToLocalTime().ToString("g", (_localizer as Localizer)?.Culture);

    private void Build()
    {
        Repairs.Clear();
        foreach (var kind in Enum.GetValues<RepairKind>())
        {
            Repairs.Add(new RepairRow(kind, T($"Repair.{kind}.Title"), T($"Repair.{kind}.Explanation")));
        }

        if (_report is not { } r)
        {
            return;
        }

        Fill(BlueScreens, r.BlueScreens.Value, b => new DiagRow(Date(b.Date), b.CodeName, b.Parameters, T($"Diag.Bsod.Explanation.{b.Category}")));
        Fill(AppCrashes, r.AppCrashes.Value, a => new AppCrashRow(a.App, a.Crashes + a.Hangs, a.TopModule ?? "—", Date(a.Last)));
        Fill(Devices, r.Devices.Value, d => new DiagRow(string.Empty, d.Name, $"{d.Code}",
            T(d.Code is 1 or 3 or 10 or 12 or 14 or 18 or 19 or 21 or 24 or 28 or 31 or 32 or 37 or 39 or 43 or 47 or 48 or 52
                ? $"Diag.Device.Code.{d.Code}" : "Diag.Device.Code.Other")));
        Fill(Disks, r.Disks.Value, d => new DiskRow(d.Name, T($"Diag.Health.{d.Health}"),
            d.WearPercent is { } w ? T("Diag.Percent", w) : "—", d.TemperatureC is { } t ? T("Diag.Celsius", t) : "—"));
        Fill(OldDrivers, r.OldDrivers.Value, d => new DiagRow(d.Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            d.Device, d.Provider, d.Version ?? string.Empty));
        DisksUnavailable = !r.Disks.Available;
        DevicesUnavailable = !r.Devices.Available;
        RebootPending = r.RebootPending.Value;
        ShutdownsText = T("Diag.ShutdownsCount", r.UnexpectedShutdowns.Value);
        OnPropertyChanged(nameof(HasLoaded));
    }

    private static void Fill<TItem, TRow>(ObservableCollection<TRow> target, IReadOnlyList<TItem>? items, Func<TItem, TRow> map)
    {
        target.Clear();
        foreach (var item in items ?? [])
        {
            target.Add(map(item));
        }
    }
}
