using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Settings;

namespace PCDoc.Presentation.Settings;

/// <summary>Jour proposé pour l'entretien automatique.</summary>
public sealed record DayChoice(DayOfWeek Day, string Label);

/// <summary>Entretien automatique (tâche planifiée) et version portable sur clé USB.</summary>
public sealed partial class SettingsPageViewModel
{
    [ObservableProperty]
    private bool _maintenanceEnabled;

    [ObservableProperty]
    private DayOfWeek _maintenanceDay = DayOfWeek.Sunday;

    [ObservableProperty]
    private int _maintenanceHour = 12;

    [ObservableProperty]
    private bool _maintenanceCleanup = true;

    [ObservableProperty]
    private bool _maintenanceSoftware = true;

    [ObservableProperty]
    private bool _maintenanceWindows = true;

    [ObservableProperty]
    private bool _maintenanceDefender = true;

    [ObservableProperty]
    private string? _maintenanceStatus;

    [ObservableProperty]
    private string? _portableStatus;

    [ObservableProperty]
    private string? _selectedDrive;

    private IMaintenanceScheduler? _scheduler;

    /// <summary>Planificateur (null = fonction indisponible). À l'affectation, l'état actuel est relu.</summary>
    public IMaintenanceScheduler? Scheduler
    {
        get => _scheduler;
        init
        {
            _scheduler = value;
            var options = _store.Load().Maintenance ?? new MaintenanceOptions();
            MaintenanceDay = options.Day;
            MaintenanceHour = options.Hour;
            MaintenanceCleanup = options.Cleanup;
            MaintenanceSoftware = options.SoftwareUpdates;
            MaintenanceWindows = options.WindowsUpdates;
            MaintenanceDefender = options.DefenderScan;
            // schtasks peut prendre quelques secondes : lu hors du fil de l'interface.
            var context = SynchronizationContext.Current;
            _ = Task.Run(SafeIsScheduled).ContinueWith(t =>
            {
                if (context is null)
                {
                    MaintenanceEnabled = t.Result;
                }
                else
                {
                    context.Post(_ => MaintenanceEnabled = t.Result, null);
                }
            }, TaskScheduler.Default);
        }
    }

    /// <summary>Racines des clés USB branchées (version portable).</summary>
    public Func<IReadOnlyList<string>>? RemovableRoots { get; init; }

    public ObservableCollection<string> Drives { get; } = [];

#pragma warning disable CA1822 // propriétés liées par le XAML
    public bool IsPortable => AppPaths.IsPortable;

    public IReadOnlyList<DayChoice> Days =>
        [.. Enum.GetValues<DayOfWeek>().Select(d => new DayChoice(d, System.Globalization.CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(d)))];

#pragma warning restore CA1822

    public IReadOnlyList<int> Hours { get; } = [.. Enumerable.Range(0, 24)];

    [RelayCommand]
    private void SaveMaintenance()
    {
        if (_scheduler is null)
        {
            return;
        }

        var options = new MaintenanceOptions(MaintenanceDay, MaintenanceHour, MaintenanceCleanup, MaintenanceSoftware, MaintenanceWindows, MaintenanceDefender);
        try
        {
            _store.Save(_store.Load() with { Maintenance = options });
            if (!MaintenanceEnabled)
            {
                _scheduler.Unschedule();
                MaintenanceStatus = _localizer["Maintenance.Disabled"];
                return;
            }

            if (_scheduler.BlockedReason() is { } reason)
            {
                MaintenanceStatus = _localizer[reason];
                return;
            }

            _scheduler.Schedule(MaintenanceDay, MaintenanceHour);
            MaintenanceStatus = _localizer.Format(new LocalizedText("Maintenance.Scheduled",
                Days.First(d => d.Day == MaintenanceDay).Label, MaintenanceHour));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or TimeoutException
                                       or global::System.ComponentModel.Win32Exception)
        {
            MaintenanceStatus = _localizer["Maintenance.Failed"];
        }
    }

    /// <summary>Copie PCDoc dans Program Files (nécessaire pour l'entretien automatique).</summary>
    [RelayCommand]
    private void InstallToProgramFiles()
    {
        if (_scheduler is null)
        {
            return;
        }

        try
        {
            MaintenanceStatus = _localizer.Format(new LocalizedText("Maintenance.Installed", _scheduler.InstallToProgramFiles()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MaintenanceStatus = _localizer["Maintenance.InstallFailed"];
        }
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        Drives.Clear();
        foreach (var root in RemovableRoots?.Invoke() ?? [])
        {
            Drives.Add(root);
        }

        SelectedDrive = Drives.FirstOrDefault();
        PortableStatus = Drives.Count == 0 ? _localizer["Portable.NoDrive"] : null;
    }

    [RelayCommand]
    private void CopyPortable()
    {
        if (_scheduler is null || SelectedDrive is null)
        {
            return;
        }

        try
        {
            PortableStatus = _localizer.Format(new LocalizedText("Portable.Done", _scheduler.CopyPortable(SelectedDrive)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            PortableStatus = _localizer["Portable.Failed"];
        }
    }

    private bool SafeIsScheduled()
    {
        try
        {
            return _scheduler?.IsScheduled() ?? false;
        }
#pragma warning disable CA1031 // planificateur indisponible : simplement « non planifié »
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
