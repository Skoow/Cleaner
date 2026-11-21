using System.Globalization;
using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.Diagnostic;

/// <summary>Source du diagnostic (simulable dans les tests de la page).</summary>
public interface IDiagnosticSource
{
    DiagnosticReport Collect();
}

/// <summary>Lit écrans bleus, plantages, périphériques en erreur, santé des disques, redémarrage en attente, vieux pilotes.</summary>
public sealed class DiagnosticCollector(IEventLogQuery log, IWmiQuery wmi, IRegistryReader registry, IClock clock)
    : IDiagnosticSource
{
    public const string WerProvider = "Microsoft-Windows-WER-SystemErrorReporting";
    public const string KernelPowerProvider = "Microsoft-Windows-Kernel-Power";
    public const string AppErrorProvider = "Application Error";
    public const string AppHangProvider = "Application Hang";
    private const string Cimv2 = @"root\cimv2";
    private const string Storage = @"root\Microsoft\Windows\Storage";
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public DiagnosticReport Collect() => new(
        Safe(ReadBlueScreens),
        Safe(ReadUnexpectedShutdowns),
        Safe(ReadAppCrashes),
        Safe(ReadDevices),
        Safe(ReadDisks),
        Safe(ReadRebootPending),
        Safe(ReadOldDrivers));

    private static Section<T> Safe<T>(Func<T> read)
    {
        try
        {
            return Section<T>.Of(read());
        }
        catch (WmiUnavailableException ex)
        {
            return Section<T>.Unavailable(ex.IsAccessDenied ? "access-denied" : "not-available");
        }
    }

    private DateTimeOffset Since => clock.Now - Window;

    private IReadOnlyList<BlueScreen> ReadBlueScreens() =>
    [
        .. log.Query("System", [1001], 100, WerProvider)
            .Where(e => e.TimeCreated >= Since && e.Data.ContainsKey("param1"))
            .Select(e =>
            {
                var param1 = e.Data["param1"].Trim();
                var codeText = param1.Split(' ', 2)[0];
                var code = uint.TryParse(codeText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? codeText[2..] : codeText,
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c) ? c : 0;
                var (name, category) = BugCheckCodes.Describe(code);
                var parameters = param1.Contains('(', StringComparison.Ordinal) ? param1[param1.IndexOf('(', StringComparison.Ordinal)..] : string.Empty;
                return new BlueScreen(e.TimeCreated, code, name, category, parameters, e.Data.GetValueOrDefault("param2"));
            }),
    ];

    private int ReadUnexpectedShutdowns() =>
        log.Query("System", [41], 100, KernelPowerProvider)
            .Count(e => e.TimeCreated >= Since && (!e.Data.TryGetValue("BugcheckCode", out var b) || b == "0"));

    private IReadOnlyList<AppCrashGroup> ReadAppCrashes()
    {
        var crashes = log.Query("Application", [1000], 500, AppErrorProvider).Where(e => e.TimeCreated >= Since)
            .Select(e => (App: e.Data.GetValueOrDefault("AppName") ?? e.Data.GetValueOrDefault("#0"),
                Module: e.Data.GetValueOrDefault("ModuleName") ?? e.Data.GetValueOrDefault("#3"), Hang: false, e.TimeCreated));
        var hangs = log.Query("Application", [1002], 500, AppHangProvider).Where(e => e.TimeCreated >= Since)
            .Select(e => (App: e.Data.GetValueOrDefault("#0"), Module: (string?)null, Hang: true, e.TimeCreated));

        return [.. crashes.Concat(hangs)
            .Where(x => !string.IsNullOrWhiteSpace(x.App))
            .GroupBy(x => x.App!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppCrashGroup(
                g.First().App!,
                g.Count(x => !x.Hang),
                g.Count(x => x.Hang),
                g.Where(x => x.Module is not null).GroupBy(x => x.Module!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(m => m.Count()).Select(m => m.Key).FirstOrDefault(),
                g.Max(x => x.TimeCreated)))
            .OrderByDescending(g => g.Crashes + g.Hangs)
            .ThenBy(g => g.App, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>22 = désactivé par l'utilisateur, 45 = débranché : pas des erreurs.</summary>
    private IReadOnlyList<DeviceProblem> ReadDevices() =>
    [
        .. wmi.Query(Cimv2, "SELECT Name, ConfigManagerErrorCode, PNPDeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Select(r => new DeviceProblem(r.GetValueOrDefault("Name") as string ?? "?",
                Convert.ToUInt32(r.GetValueOrDefault("ConfigManagerErrorCode") ?? 0u, CultureInfo.InvariantCulture),
                r.GetValueOrDefault("PNPDeviceID") as string))
            .Where(d => d.Code is not (0 or 22 or 45)),
    ];

    private IReadOnlyList<DiskHealth> ReadDisks()
    {
        var disks = wmi.Query(Storage, "SELECT DeviceId, FriendlyName, HealthStatus FROM MSFT_PhysicalDisk");
        IReadOnlyList<IReadOnlyDictionary<string, object?>> counters;
        try
        {
            counters = wmi.Query(Storage, "SELECT DeviceId, Temperature, Wear, ReadErrorsUncorrected FROM MSFT_StorageReliabilityCounter");
        }
        catch (WmiUnavailableException)
        {
            counters = [];
        }

        return [.. disks.Select(d =>
        {
            var id = Convert.ToString(d.GetValueOrDefault("DeviceId"), CultureInfo.InvariantCulture);
            var c = counters.FirstOrDefault(x => Convert.ToString(x.GetValueOrDefault("DeviceId"), CultureInfo.InvariantCulture) == id);
            return new DiskHealth(
                d.GetValueOrDefault("FriendlyName") as string ?? "?",
                Number(d.GetValueOrDefault("HealthStatus")) switch { 0 => DiskHealthStatus.Healthy, 1 => DiskHealthStatus.Warning, 2 => DiskHealthStatus.Unhealthy, _ => DiskHealthStatus.Unknown },
                (int?)Number(c?.GetValueOrDefault("Wear")),
                (int?)Number(c?.GetValueOrDefault("Temperature")) is { } t and > 0 ? t : null,
                Number(c?.GetValueOrDefault("ReadErrorsUncorrected")));
        })];
    }

    private bool ReadRebootPending() =>
        registry.GetSubKeyNames(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending").Count > 0
        || registry.GetValueNames(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending").Count > 0
        || registry.GetValueNames(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired").Count > 0
        || registry.GetValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations") is string[] { Length: > 0 };

    private IReadOnlyList<OldDriver> ReadOldDrivers()
    {
        var limit = DateOnly.FromDateTime(clock.Now.UtcDateTime).AddYears(-5);
        return [.. wmi.Query(Cimv2, "SELECT DeviceName, DriverProviderName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver")
            .Select(r => (Device: r.GetValueOrDefault("DeviceName") as string, Provider: r.GetValueOrDefault("DriverProviderName") as string,
                Version: r.GetValueOrDefault("DriverVersion") as string, Date: CimDate(r.GetValueOrDefault("DriverDate"))))
            .Where(d => d.Device is not null && d.Date is { } date && date < limit
                        && d.Provider is not null && !d.Provider.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
            .Select(d => new OldDriver(d.Device!, d.Provider!, d.Version, d.Date!.Value))
            .OrderBy(d => d.Date)];
    }

    private static long? Number(object? value) => value switch
    {
        null => null,
        IConvertible c => c.ToInt64(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static DateOnly? CimDate(object? value) =>
        value is string { Length: >= 8 } s && DateOnly.TryParseExact(s[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
