namespace PCDoc.Modules.System.Inventory;

#pragma warning disable CA1000 // Fabriques statiques : Section<T>.Of(...) se lit mieux.
/// <summary>Une section de l'inventaire : la valeur, ou la raison pour laquelle elle n'est pas disponible.</summary>
public sealed record Section<T>(T? Value, bool Available, string? Reason)
{
    public static Section<T> Of(T value) => new(value, true, null);

    public static Section<T> Unavailable(string reason) => new(default, false, reason);
}
#pragma warning restore CA1000

/// <summary>État d'activation de Windows.</summary>
public enum ActivationState
{
    Unknown,
    Activated,
    NotActivated,
}

public sealed record WindowsInfo(
    string Name,
    string Edition,
    string? DisplayVersion,
    int Build,
    int Ubr,
    DateTimeOffset? InstallDate,
    ActivationState Activation)
{
    public bool IsWindows10 => Build < WindowsVersionNamer.FirstWindows11Build;
}

public sealed record CpuInfo(string Name, int Cores, int LogicalProcessors, int MaxClockMHz);

public sealed record MemoryModule(long CapacityBytes, int SpeedMHz, string? Manufacturer, string? Locator);

public sealed record MemoryInfo(long TotalBytes, long UsedBytes, IReadOnlyList<MemoryModule> Modules);

public sealed record GpuInfo(string Name, string? DriverVersion, long? MemoryBytes);

public sealed record BoardInfo(string? Manufacturer, string? Product);

public sealed record BiosInfo(string? Manufacturer, string? Version, DateOnly? ReleaseDate);

/// <summary>Type de disque (MSFT_PhysicalDisk.MediaType : 3 = HDD, 4 = SSD, 5 = SCM).</summary>
public enum DiskMediaType
{
    Unknown,
    Hdd,
    Ssd,
    Scm,
}

public sealed record DiskInfo(string Name, long SizeBytes, DiskMediaType MediaType, bool IsNvme);

public sealed record VolumeInfo(string Letter, long SizeBytes, long FreeBytes, string? FileSystem)
{
    public double FreeRatio => SizeBytes <= 0 ? 0 : (double)FreeBytes / SizeBytes;
}

public sealed record DisplayInfo(string? Name, double? DiagonalInches);

public sealed record BatteryInfo(string? Name, long DesignedCapacity, long FullChargedCapacity)
{
    /// <summary>Usure : 1 − capacité pleine / capacité d'origine (0 si inconnue).</summary>
    public double Wear => DesignedCapacity <= 0 ? 0 : Math.Clamp(1 - (double)FullChargedCapacity / DesignedCapacity, 0, 1);
}

/// <summary>Résultat d'un contrôle de compatibilité.</summary>
public enum CheckState
{
    Pass,
    Fail,
    Unknown,
}

/// <summary>Compatibilité Windows 11 (spec v2 §M2).</summary>
public sealed record Win11Readiness(
    CheckState Tpm20,
    CheckState SecureBoot,
    CheckState Uefi,
    CheckState SystemDiskGpt,
    CheckState CpuSupported,
    CheckState Ram4GB,
    CheckState Storage64GB)
{
    public IEnumerable<(string Check, CheckState State)> Checks =>
    [
        (nameof(Tpm20), Tpm20), (nameof(SecureBoot), SecureBoot), (nameof(Uefi), Uefi),
        (nameof(SystemDiskGpt), SystemDiskGpt), (nameof(CpuSupported), CpuSupported),
        (nameof(Ram4GB), Ram4GB), (nameof(Storage64GB), Storage64GB),
    ];

    public bool AllPass => Checks.All(c => c.State == CheckState.Pass);

    public IReadOnlyList<string> Failed => [.. Checks.Where(c => c.State == CheckState.Fail).Select(c => c.Check)];
}

/// <summary>Inventaire complet du PC.</summary>
public sealed record SystemInventory(
    Section<WindowsInfo> Windows,
    Section<CpuInfo> Cpu,
    Section<MemoryInfo> Memory,
    Section<IReadOnlyList<GpuInfo>> Gpus,
    Section<BoardInfo> Board,
    Section<BiosInfo> Bios,
    Section<IReadOnlyList<DiskInfo>> Disks,
    Section<IReadOnlyList<VolumeInfo>> Volumes,
    Section<IReadOnlyList<DisplayInfo>> Displays,
    Section<IReadOnlyList<BatteryInfo>> Batteries,
    Section<Win11Readiness> Win11);
