using PCDoc.Core.Abstractions;
using static PCDoc.Modules.System.Inventory.WmiValues;

namespace PCDoc.Modules.System.Inventory;

/// <summary>Source de l'inventaire (simulable dans les tests de la page Système).</summary>
public interface IInventorySource
{
    SystemInventory Collect();
}

/// <summary>
/// Inventaire Windows et matériel en lecture seule. Chaque section est indépendante :
/// une source absente ou refusée donne « Non disponible » pour cette section seulement.
/// </summary>
public sealed class InventoryCollector(IWmiQuery wmi, IRegistryReader registry, Win11CpuList cpuList) : IInventorySource
{
    private const string Cimv2 = @"root\cimv2";
    private const string RootWmi = @"root\wmi";
    private const string Storage = @"root\Microsoft\Windows\Storage";
    private const string Tpm = @"root\cimv2\Security\MicrosoftTpm";
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string SecureBootState = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";
    private const long FourGiB = 4L * 1024 * 1024 * 1024;
    private const long SixtyFourGB = 64L * 1000 * 1000 * 1000;

    public SystemInventory Collect()
    {
        var memory = Safe(ReadMemory);
        return new SystemInventory(
            Safe(ReadWindows),
            Safe(ReadCpu),
            memory,
            Safe(ReadGpus),
            Safe(() => FirstRow(Cimv2, "SELECT Manufacturer, Product FROM Win32_BaseBoard",
                r => new BoardInfo(Str(r.GetValueOrDefault("Manufacturer")), Str(r.GetValueOrDefault("Product"))))),
            Safe(() => FirstRow(Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS",
                r => new BiosInfo(Str(r.GetValueOrDefault("Manufacturer")), Str(r.GetValueOrDefault("SMBIOSBIOSVersion")),
                    CimDate(r.GetValueOrDefault("ReleaseDate"))))),
            Safe(ReadDisks),
            Safe(ReadVolumes),
            Safe(ReadDisplays),
            Safe(ReadBatteries),
            Section<Win11Readiness>.Of(ReadWin11(memory)));
    }

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
        catch (InvalidOperationException ex)
        {
            return Section<T>.Unavailable(ex.Message);
        }
    }

    private T FirstRow<T>(string ns, string wql, Func<IReadOnlyDictionary<string, object?>, T> map)
    {
        var rows = wmi.Query(ns, wql);
        return rows.Count == 0 ? throw new InvalidOperationException("no-data") : map(rows[0]);
    }

    private WindowsInfo ReadWindows()
    {
        var build = Int(Long(registry.GetValue(RegistryHive.LocalMachine, CurrentVersion, "CurrentBuild")));
        var installSeconds = Long(registry.GetValue(RegistryHive.LocalMachine, CurrentVersion, "InstallDate"));
        return new WindowsInfo(
            WindowsVersionNamer.Name(build),
            WindowsVersionNamer.Edition(Str(registry.GetValue(RegistryHive.LocalMachine, CurrentVersion, "ProductName")), build),
            Str(registry.GetValue(RegistryHive.LocalMachine, CurrentVersion, "DisplayVersion")),
            build,
            Int(registry.GetValue(RegistryHive.LocalMachine, CurrentVersion, "UBR")),
            installSeconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds((uint)installSeconds.Value).ToLocalTime() : null,
            ReadActivation());
    }

    private ActivationState ReadActivation()
    {
        try
        {
            var rows = wmi.Query(Cimv2,
                $"SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID='{WindowsAppId}' AND PartialProductKey IS NOT NULL");
            if (rows.Count == 0)
            {
                return ActivationState.Unknown;
            }

            return rows.Any(r => Long(r.GetValueOrDefault("LicenseStatus")) == 1)
                ? ActivationState.Activated
                : ActivationState.NotActivated;
        }
        catch (WmiUnavailableException)
        {
            return ActivationState.Unknown;
        }
    }

    private CpuInfo ReadCpu() => FirstRow(Cimv2,
        "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor",
        r => new CpuInfo(Str(r.GetValueOrDefault("Name")) ?? "?", Int(r.GetValueOrDefault("NumberOfCores")),
            Int(r.GetValueOrDefault("NumberOfLogicalProcessors")), Int(r.GetValueOrDefault("MaxClockSpeed"))));

    private MemoryInfo ReadMemory()
    {
        var os = FirstRow(Cimv2, "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem", r => r);
        var total = (Long(os.GetValueOrDefault("TotalVisibleMemorySize")) ?? 0) * 1024;
        var free = (Long(os.GetValueOrDefault("FreePhysicalMemory")) ?? 0) * 1024;
        IReadOnlyList<MemoryModule> modules;
        try
        {
            modules = [.. wmi.Query(Cimv2, "SELECT Capacity, ConfiguredClockSpeed, Speed, Manufacturer, DeviceLocator FROM Win32_PhysicalMemory")
                .Select(r => new MemoryModule(Long(r.GetValueOrDefault("Capacity")) ?? 0,
                    Int(r.GetValueOrDefault("ConfiguredClockSpeed") ?? r.GetValueOrDefault("Speed")),
                    Str(r.GetValueOrDefault("Manufacturer")), Str(r.GetValueOrDefault("DeviceLocator"))))];
        }
        catch (WmiUnavailableException)
        {
            modules = [];
        }

        return new MemoryInfo(total, Math.Max(0, total - free), modules);
    }

    private IReadOnlyList<GpuInfo> ReadGpus() =>
    [
        .. wmi.Query(Cimv2, "SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController").Select(r =>
        {
            var name = Str(r.GetValueOrDefault("Name")) ?? "?";
            var adapterRam = Long(r.GetValueOrDefault("AdapterRAM"));
            return new GpuInfo(name, Str(r.GetValueOrDefault("DriverVersion")), RegistryGpuMemory(name) ?? adapterRam);
        }),
    ];

    /// <summary>AdapterRAM plafonne à 4 Gio : la vraie taille est dans le registre de la classe Affichage.</summary>
    private long? RegistryGpuMemory(string name)
    {
        foreach (var sub in registry.GetSubKeyNames(RegistryHive.LocalMachine, DisplayClass))
        {
            var path = $@"{DisplayClass}\{sub}";
            if (string.Equals(Str(registry.GetValue(RegistryHive.LocalMachine, path, "DriverDesc")), name, StringComparison.OrdinalIgnoreCase))
            {
                return registry.GetValue(RegistryHive.LocalMachine, path, "HardwareInformation.qwMemorySize") switch
                {
                    byte[] { Length: 8 } bytes => BitConverter.ToInt64(bytes),
                    var other => Long(other),
                };
            }
        }

        return null;
    }

    private IReadOnlyList<DiskInfo> ReadDisks() =>
    [
        .. wmi.Query(Storage, "SELECT FriendlyName, Size, MediaType, BusType FROM MSFT_PhysicalDisk").Select(r =>
            new DiskInfo(
                Str(r.GetValueOrDefault("FriendlyName")) ?? "?",
                Long(r.GetValueOrDefault("Size")) ?? 0,
                Long(r.GetValueOrDefault("MediaType")) switch { 3 => DiskMediaType.Hdd, 4 => DiskMediaType.Ssd, 5 => DiskMediaType.Scm, _ => DiskMediaType.Unknown },
                Long(r.GetValueOrDefault("BusType")) == 17)),
    ];

    private IReadOnlyList<VolumeInfo> ReadVolumes() =>
    [
        .. wmi.Query(Cimv2, "SELECT DeviceID, Size, FreeSpace, FileSystem FROM Win32_LogicalDisk WHERE DriveType=3").Select(r =>
            new VolumeInfo(Str(r.GetValueOrDefault("DeviceID")) ?? "?", Long(r.GetValueOrDefault("Size")) ?? 0,
                Long(r.GetValueOrDefault("FreeSpace")) ?? 0, Str(r.GetValueOrDefault("FileSystem")))),
    ];

    private IReadOnlyList<DisplayInfo> ReadDisplays()
    {
        var ids = wmi.Query(RootWmi, "SELECT UserFriendlyName FROM WmiMonitorID");
        IReadOnlyList<IReadOnlyDictionary<string, object?>> sizes;
        try
        {
            sizes = wmi.Query(RootWmi, "SELECT MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams");
        }
        catch (WmiUnavailableException)
        {
            sizes = [];
        }

        return [.. ids.Select((r, i) =>
        {
            double? diagonal = null;
            if (i < sizes.Count)
            {
                var w = Long(sizes[i].GetValueOrDefault("MaxHorizontalImageSize")) ?? 0;
                var h = Long(sizes[i].GetValueOrDefault("MaxVerticalImageSize")) ?? 0;
                diagonal = w > 0 && h > 0 ? Math.Round(Math.Sqrt((w * w) + (h * h)) / 2.54, 1) : null;
            }

            return new DisplayInfo(CharCodes(r.GetValueOrDefault("UserFriendlyName")), diagonal);
        })];
    }

    private IReadOnlyList<BatteryInfo> ReadBatteries()
    {
        var present = wmi.Query(Cimv2, "SELECT Name FROM Win32_Battery");
        if (present.Count == 0)
        {
            return []; // PC fixe : liste vide, mais section disponible.
        }

        var designed = wmi.Query(RootWmi, "SELECT DesignedCapacity FROM BatteryStaticData");
        var full = wmi.Query(RootWmi, "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
        return [.. present.Select((r, i) => new BatteryInfo(
            Str(r.GetValueOrDefault("Name")),
            i < designed.Count ? Long(designed[i].GetValueOrDefault("DesignedCapacity")) ?? 0 : 0,
            i < full.Count ? Long(full[i].GetValueOrDefault("FullChargedCapacity")) ?? 0 : 0))];
    }

    private Win11Readiness ReadWin11(Section<MemoryInfo> memory)
    {
        var (gpt, diskSize) = ReadSystemDisk();
        var secureBoot = registry.GetValue(RegistryHive.LocalMachine, SecureBootState, "UEFISecureBootEnabled");
        var cpuName = Safe(ReadCpu).Value?.Name;

        var installed = memory.Value?.Modules.Sum(m => m.CapacityBytes) ?? 0;
        var ram = installed > 0 ? installed : memory.Value?.TotalBytes ?? 0;

        return new Win11Readiness(
            ReadTpm(),
            secureBoot is null ? CheckState.Fail : Long(secureBoot) == 1 ? CheckState.Pass : CheckState.Fail,
            secureBoot is null ? CheckState.Fail : CheckState.Pass,
            gpt,
            cpuList.Check(cpuName),
            ram <= 0 ? CheckState.Unknown : ram >= FourGiB ? CheckState.Pass : CheckState.Fail,
            diskSize is null ? CheckState.Unknown : diskSize >= SixtyFourGB ? CheckState.Pass : CheckState.Fail);
    }

    private CheckState ReadTpm()
    {
        try
        {
            var rows = wmi.Query(Tpm, "SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm");
            return rows.Any(r => Str(r.GetValueOrDefault("SpecVersion"))?.StartsWith("2.0", StringComparison.Ordinal) == true
                                 && r.GetValueOrDefault("IsEnabled_InitialValue") is true)
                ? CheckState.Pass
                : CheckState.Fail;
        }
        catch (WmiUnavailableException ex)
        {
            return ex.IsAccessDenied ? CheckState.Unknown : CheckState.Fail;
        }
    }

    private (CheckState Gpt, long? Size) ReadSystemDisk()
    {
        try
        {
            var partition = wmi.Query(Storage, "SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter='C'");
            if (partition.Count == 0)
            {
                return (CheckState.Unknown, null);
            }

            var number = Long(partition[0].GetValueOrDefault("DiskNumber"));
            var disks = wmi.Query(Storage, $"SELECT Number, PartitionStyle, Size FROM MSFT_Disk WHERE Number={number}");
            var disk = disks.FirstOrDefault(d => Long(d.GetValueOrDefault("Number")) == number);
            if (disk is null)
            {
                return (CheckState.Unknown, null);
            }

            return (Long(disk.GetValueOrDefault("PartitionStyle")) == 2 ? CheckState.Pass : CheckState.Fail,
                Long(disk.GetValueOrDefault("Size")));
        }
        catch (WmiUnavailableException)
        {
            return (CheckState.Unknown, null);
        }
    }
}
