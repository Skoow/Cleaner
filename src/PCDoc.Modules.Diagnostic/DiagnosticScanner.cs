using System.Globalization;
using System.Resources;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Diagnostic;

/// <summary>Point d'entrée du module Diagnostic (ressources de traduction).</summary>
public static class DiagnosticModule
{
    public const string Id = "diagnostic";

    public static ResourceManager Resources { get; } =
        new("PCDoc.Modules.Diagnostic.Resources.Strings", typeof(DiagnosticModule).Assembly);

    public static void RegisterResources(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        localizer.Register(Resources);
    }
}

/// <summary>Règles du Bilan pour le Diagnostic (lecture seule).</summary>
public sealed class DiagnosticScanner(IDiagnosticSource source) : IScanner
{
    private static readonly uint[] KnownDeviceCodes = [1, 3, 10, 12, 14, 18, 19, 21, 24, 28, 31, 32, 37, 39, 43, 47, 48, 52];

    public string ModuleId => DiagnosticModule.Id;

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken)
    {
        var report = await Task.Run(source.Collect, cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();
        var failed = new List<string>();

        void From<T>(string name, Section<T> section, Action<T> rule)
        {
            if (section is { Available: true, Value: { } value })
            {
                rule(value);
            }
            else if (!section.Available)
            {
                failed.Add(name);
            }
        }

        From("bluescreens", report.BlueScreens, bsod =>
        {
            if (bsod.Count == 0)
            {
                return;
            }

            var main = bsod.GroupBy(b => b.Category).OrderByDescending(g => g.Count()).First().Key;
            findings.Add(Make("diagnostic.bsod", bsod.Count >= 3 ? Severity.Critical : Severity.Warning,
                Text("Diag.Bsod.Title", bsod.Count), Text($"Diag.Bsod.Explanation.{main}"),
                string.Join("\n", bsod.Select(b => FormattableString.Invariant($"{b.Date:yyyy-MM-dd HH:mm}  0x{b.Code:X8} {b.CodeName} {b.Parameters} {b.DumpFile}")))));
        });

        From("shutdowns", report.UnexpectedShutdowns, count =>
        {
            if (count >= 3)
            {
                findings.Add(Make("diagnostic.shutdowns", Severity.Info, Text("Diag.Shutdowns.Title", count), Text("Diag.Shutdowns.Explanation"),
                    $"Microsoft-Windows-Kernel-Power 41 (BugcheckCode 0) × {count}, 30 days"));
            }
        });

        From("appcrashes", report.AppCrashes, apps =>
        {
            foreach (var app in apps.Where(a => a.Crashes + a.Hangs >= 3))
            {
                findings.Add(Make($"diagnostic.app-crash.{Slug(app.App)}", Severity.Warning,
                    Text("Diag.AppCrash.Title", app.App, app.Crashes + app.Hangs), Text("Diag.AppCrash.Explanation"),
                    $"Application Error 1000 × {app.Crashes}, Application Hang 1002 × {app.Hangs}\nModule: {app.TopModule ?? "-"}"));
            }
        });

        From("devices", report.Devices, devices =>
        {
            foreach (var d in devices)
            {
                var key = KnownDeviceCodes.Contains(d.Code) ? $"Diag.Device.Code.{d.Code}" : "Diag.Device.Code.Other";
                findings.Add(Make($"diagnostic.device.{Slug(d.DeviceId ?? d.Name)}", Severity.Warning,
                    Text("Diag.Device.Title", d.Name, d.Code), Text(key), $"Win32_PnPEntity {d.DeviceId}\nConfigManagerErrorCode = {d.Code}"));
            }
        });

        From("disks", report.Disks, disks =>
        {
            foreach (var d in disks)
            {
                var critical = d.Health == DiskHealthStatus.Unhealthy || d.UncorrectedReadErrors > 0;
                var warning = d.Health == DiskHealthStatus.Warning || d.WearPercent >= 90;
                if (critical || warning)
                {
                    findings.Add(Make($"diagnostic.disk.{Slug(d.Name)}", critical ? Severity.Critical : Severity.Warning,
                        Text("Diag.Disk.Title", d.Name), Text(critical ? "Diag.Disk.Explanation.Critical" : "Diag.Disk.Explanation.Warning"),
                        FormattableString.Invariant($"HealthStatus = {d.Health}\nWear = {d.WearPercent?.ToString(CultureInfo.InvariantCulture) ?? "-"} %\nTemperature = {d.TemperatureC?.ToString(CultureInfo.InvariantCulture) ?? "-"} °C\nReadErrorsUncorrected = {d.UncorrectedReadErrors?.ToString(CultureInfo.InvariantCulture) ?? "-"}")));
                }
            }
        });

        From("reboot", report.RebootPending, pending =>
        {
            if (pending)
            {
                findings.Add(Make("diagnostic.reboot-pending", Severity.Info, Text("Diag.Reboot.Title"), Text("Diag.Reboot.Explanation"),
                    "Component Based Servicing\\RebootPending / WindowsUpdate\\Auto Update\\RebootRequired / PendingFileRenameOperations"));
            }
        });

        From("drivers", report.OldDrivers, drivers =>
        {
            if (drivers.Count > 0)
            {
                findings.Add(Make("diagnostic.old-drivers", Severity.Info, Text("Diag.OldDrivers.Title", drivers.Count), Text("Diag.OldDrivers.Explanation"),
                    string.Join("\n", drivers.Select(d => FormattableString.Invariant($"{d.Date:yyyy-MM-dd}  {d.Provider}  {d.Device}  {d.Version}")))));
            }
        });

        IReadOnlyList<Finding> result = findings;
        return failed.Count == 0 ? result : throw new PartialScanException(result, failed);
    }

    private static Finding Make(string id, Severity severity, LocalizedText title, LocalizedText explanation, string details) =>
        new(id, DiagnosticModule.Id, severity, title, explanation, details);

    private static LocalizedText Text(string key, params object[] args) => new(key, args);

    private static string Slug(string value) =>
        new string([.. value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '-')]).Trim('-');
}
