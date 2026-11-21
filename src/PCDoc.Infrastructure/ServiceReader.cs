using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure;

/// <summary>Services (Win32_Service) et pilotes (Win32_SystemDriver) ; démarrage différé lu dans le registre.</summary>
public sealed class ServiceReader(IWmiQuery wmi, IRegistryReader registry) : IServiceReader
{
    private const string Cimv2 = @"root\cimv2";

    public IReadOnlyList<ServiceInfo> ReadAll()
    {
        var results = new List<ServiceInfo>();
        Read("SELECT Name, DisplayName, StartMode, State, PathName, Description FROM Win32_Service", isDriver: false, results);
        Read("SELECT Name, DisplayName, StartMode, State, PathName, Description FROM Win32_SystemDriver", isDriver: true, results);
        return results;
    }

    private void Read(string wql, bool isDriver, List<ServiceInfo> results)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            rows = wmi.Query(Cimv2, wql);
        }
        catch (WmiUnavailableException)
        {
            return;
        }

        foreach (var row in rows)
        {
            var name = row.GetValueOrDefault("Name") as string;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var mode = MapStartMode(row.GetValueOrDefault("StartMode") as string);
            if (mode == ServiceStartMode.Automatic && !isDriver && IsDelayed(name))
            {
                mode = ServiceStartMode.AutomaticDelayed;
            }

            results.Add(new ServiceInfo(
                name,
                row.GetValueOrDefault("DisplayName") as string ?? name,
                mode,
                row.GetValueOrDefault("State") as string ?? string.Empty,
                row.GetValueOrDefault("PathName") as string,
                row.GetValueOrDefault("Description") as string,
                isDriver));
        }
    }

    private bool IsDelayed(string name) =>
        registry.GetValue(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{name}", "DelayedAutostart") is int value
        && value == 1;

    internal static ServiceStartMode MapStartMode(string? wmiStartMode) => wmiStartMode?.ToUpperInvariant() switch
    {
        "BOOT" => ServiceStartMode.Boot,
        "SYSTEM" => ServiceStartMode.System,
        "AUTO" => ServiceStartMode.Automatic,
        "MANUAL" => ServiceStartMode.Manual,
        "DISABLED" => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown,
    };
}
