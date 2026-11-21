using System.Management;
using System.Runtime.InteropServices;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Fixing;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Points de restauration via WMI (root\default:SystemRestore). Windows refuse un nouveau point moins de 24 h après
/// le précédent : on réutilise alors le point récent (spec v2 §4.3).
/// </summary>
public sealed class RestorePointService(IClock clock) : IRestorePointService
{
    private const string Ns = @"root\default";
    private const uint ModifySettings = 12;
    private const uint BeginSystemChange = 100;

    public Task<RestorePointOutcome> EnsureAsync(string description, CancellationToken cancellationToken) =>
        Task.Run(() => Ensure(description), cancellationToken);

    /// <summary>Active la protection du système sur le disque de Windows (assistant de premier lancement).</summary>
    public static bool EnableProtection()
    {
        try
        {
            using var restore = new ManagementClass(new ManagementScope(Ns), new ManagementPath("SystemRestore"), null);
            var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var result = restore.InvokeMethod("Enable", [drive]);
            return result is uint code && code == 0;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsProtectionEnabled()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Ns, "SELECT * FROM SystemRestoreConfig");
            using var results = searcher.Get();
            foreach (var item in results)
            {
                using (item)
                {
                    // RPSessionInterval = 1 quand la protection est active sur au moins un disque.
                    return item["RPSessionInterval"] is uint interval && interval > 0;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private RestorePointOutcome Ensure(string description)
    {
        try
        {
            if (!IsProtectionEnabled())
            {
                return RestorePointOutcome.ProtectionDisabled;
            }

            if (LatestRestorePoint() is { } latest && clock.Now - latest < TimeSpan.FromHours(24))
            {
                return RestorePointOutcome.ReusedRecent;
            }

            using var restore = new ManagementClass(new ManagementScope(Ns), new ManagementPath("SystemRestore"), null);
            var result = restore.InvokeMethod("CreateRestorePoint", [description, ModifySettings, BeginSystemChange]);
            return result is uint code && code == 0 ? RestorePointOutcome.Created : RestorePointOutcome.Failed;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return RestorePointOutcome.Failed;
        }
    }

    private static DateTimeOffset? LatestRestorePoint()
    {
        using var searcher = new ManagementObjectSearcher(Ns, "SELECT CreationTime FROM SystemRestore");
        using var results = searcher.Get();
        DateTimeOffset? latest = null;
        foreach (var item in results)
        {
            using (item)
            {
                if (item["CreationTime"] is string cim)
                {
                    var created = new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(cim));
                    if (latest is null || created > latest)
                    {
                        latest = created;
                    }
                }
            }
        }

        return latest;
    }
}
