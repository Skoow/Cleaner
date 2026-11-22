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

public sealed record RepairRow(RepairKind Kind, string Title, string Explanation, string RiskLabel);

/// <summary>Résultat d'une étape d'assistant ; <paramref name="Repair"/> = réparation conseillée.</summary>
public sealed record AssistantRow(string Status, string Text, RepairKind? Repair, string? RepairTitle)
{
    public bool HasRepair => Repair is not null;
}

/// <summary>Page Diagnostic : écrans bleus, plantages, périphériques, disques, réparations, aide.</summary>
public sealed partial class DiagnosticPageViewModel : ObservableObject
{
    private readonly IDiagnosticSource _source;
    private readonly ILocalizer _localizer;
    private readonly Lock _outputGate = new();
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

    private readonly IAssistants? _assistants;

    [ObservableProperty]
    private bool _assistantRunning;

    public DiagnosticPageViewModel(IDiagnosticSource source, ILocalizer localizer, RepairFixer? repairs = null, FixConfirmationViewModel? fixes = null,
        IAssistants? assistants = null)
    {
        _assistants = assistants;
        _source = source;
        _localizer = localizer;
        Fixes = fixes;
        if (repairs is not null)
        {
            repairs.Output += (_, line) =>
            {
                lock (_outputGate) // stdout et stderr arrivent sur deux fils différents
                {
                    RepairOutput = (RepairOutput.Length > 4000 ? RepairOutput[^3000..] : RepairOutput) + line + "\n";
                }
            };
            repairs.RebootRequired += (_, _) => RebootPending = true;
        }

        if (fixes is not null)
        {
            fixes.PropertyChanged += (_, _) => RunRepairCommand.NotifyCanExecuteChanged();
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

    public ObservableCollection<AssistantRow> AssistantResults { get; } = [];

    /// <summary>Onglet Réseau (null = indisponible).</summary>
    public NetworkViewModel? Network { get; init; }

    public bool HasAssistants => _assistants is not null;

    [RelayCommand]
    private Task NoInternetAsync() => RunAssistantAsync(a => a.NoInternetAsync(CancellationToken.None));

    [RelayCommand]
    private Task NoSoundAsync() => RunAssistantAsync(a => a.NoSoundAsync(CancellationToken.None));

    private async Task RunAssistantAsync(Func<IAssistants, Task<IReadOnlyList<AssistantCheck>>> run)
    {
        if (_assistants is null || AssistantRunning)
        {
            return;
        }

        AssistantRunning = true;
        AssistantResults.Clear();
        try
        {
            foreach (var check in await Task.Run(() => run(_assistants)).ConfigureAwait(true))
            {
                AssistantResults.Add(new AssistantRow(_localizer[$"Assistant.Status.{check.Status}"], _localizer.Format(check.Text), check.Repair,
                    check.Repair is { } r ? _localizer[$"Repair.{r}.Title"] : null));
            }
        }
        finally
        {
            AssistantRunning = false;
        }
    }

    private bool CanRunRepair(RepairKind kind) => Fixes is { IsOpen: false, IsRunning: false };

    [RelayCommand(CanExecute = nameof(CanRunRepair))]
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
            Repairs.Add(new RepairRow(kind, T($"Repair.{kind}.Title"), T($"Repair.{kind}.Explanation"), T($"Risk.{RepairFixer.RiskOf(kind)}")));
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
        RebootPending = RebootPending || r.RebootPending.Value;
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
