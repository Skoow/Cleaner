using PCDoc.Core.Abstractions;
using PCDoc.Modules.System.Inventory;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.System.Tests;

public class InventoryCollectorTests
{
    internal const string Cimv2 = @"root\cimv2";
    internal const string Wmi = @"root\wmi";
    internal const string Storage = @"root\Microsoft\Windows\Storage";
    internal const string Tpm = @"root\cimv2\Security\MicrosoftTpm";
    internal const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    internal const string SecureBootState = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";

    /// <summary>Un PC complet et compatible Windows 11.</summary>
    internal static (FakeWmiQuery Wmi, FakeRegistryReader Reg) TypicalPc(int build = 22631)
    {
        var reg = new FakeRegistryReader()
            .Set(RegistryHive.LocalMachine, CurrentVersion, "ProductName", "Windows 10 Pro")
            .Set(RegistryHive.LocalMachine, CurrentVersion, "CurrentBuild", build.ToString(global::System.Globalization.CultureInfo.InvariantCulture))
            .Set(RegistryHive.LocalMachine, CurrentVersion, "UBR", 2314)
            .Set(RegistryHive.LocalMachine, CurrentVersion, "DisplayVersion", "23H2")
            .Set(RegistryHive.LocalMachine, CurrentVersion, "InstallDate", 1741777200)
            .Set(RegistryHive.LocalMachine, SecureBootState, "UEFISecureBootEnabled", 1);
        var wmi = new FakeWmiQuery()
            .Add(Cimv2, "SoftwareLicensingProduct", Row.Of(("LicenseStatus", 1u)))
            .Add(Cimv2, "Win32_Processor", Row.Of(("Name", "Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz"),
                ("NumberOfCores", 6u), ("NumberOfLogicalProcessors", 6u), ("MaxClockSpeed", 2808u)))
            .Add(Cimv2, "Win32_OperatingSystem", Row.Of(("TotalVisibleMemorySize", 16_000_000ul), ("FreePhysicalMemory", 6_000_000ul)))
            .Add(Cimv2, "Win32_PhysicalMemory",
                Row.Of(("Capacity", 8_589_934_592ul), ("ConfiguredClockSpeed", 3200u), ("Manufacturer", "Kingston"), ("DeviceLocator", "DIMM_A2")),
                Row.Of(("Capacity", 8_589_934_592ul), ("ConfiguredClockSpeed", 3200u), ("Manufacturer", "Kingston"), ("DeviceLocator", "DIMM_B2")))
            .Add(Cimv2, "Win32_VideoController", Row.Of(("Name", "NVIDIA GeForce RTX 3060"), ("DriverVersion", "32.0.15.6094"), ("AdapterRAM", 4_293_918_720u)))
            .Add(Cimv2, "Win32_BaseBoard", Row.Of(("Manufacturer", "ASUSTeK"), ("Product", "TUF GAMING B550-PLUS")))
            .Add(Cimv2, "Win32_BIOS", Row.Of(("Manufacturer", "American Megatrends"), ("SMBIOSBIOSVersion", "3607"), ("ReleaseDate", "20240815000000.000000+000")))
            .Add(Storage, "MSFT_PhysicalDisk", Row.Of(("FriendlyName", "Samsung SSD 980"), ("Size", 500_107_862_016ul), ("MediaType", (ushort)4), ("BusType", (ushort)17)))
            .Add(Cimv2, "Win32_LogicalDisk", Row.Of(("DeviceID", "C:"), ("Size", 511_101_108_224ul), ("FreeSpace", 10_522_476_544ul), ("FileSystem", "NTFS")))
            .Add(Wmi, "WmiMonitorID", Row.Of(("UserFriendlyName", new ushort[] { 68, 69, 76, 76, 0, 0 })))
            .Add(Wmi, "WmiMonitorBasicDisplayParams", Row.Of(("MaxHorizontalImageSize", (byte)60), ("MaxVerticalImageSize", (byte)34)))
            .Add(Tpm, "Win32_Tpm", Row.Of(("SpecVersion", "2.0, 0, 1.59"), ("IsEnabled_InitialValue", true)))
            .Add(Storage, "MSFT_Partition", Row.Of(("DiskNumber", 0u), ("DriveLetter", 'C')))
            .Add(Storage, "MSFT_Disk", Row.Of(("Number", 0u), ("PartitionStyle", (ushort)2), ("Size", 500_107_862_016ul)));
        return (wmi, reg);
    }

    private static SystemInventory Collect(FakeWmiQuery wmi, FakeRegistryReader reg) =>
        new InventoryCollector(wmi, reg, Win11CpuList.Default).Collect();

    [Fact]
    public void WindowsName_Build22631_IsWindows11()
    {
        WindowsVersionNamer.Name(22631).Should().Be("Windows 11");
        WindowsVersionNamer.Edition("Windows 10 Pro", 22631).Should().Be("Windows 11 Pro");
    }

    [Fact]
    public void WindowsName_Build19045_IsWindows10() => WindowsVersionNamer.Name(19045).Should().Be("Windows 10");

    [Fact]
    public void InstallDate_UnixSeconds_ConvertedToLocalDate()
    {
        var (wmi, reg) = TypicalPc();

        var windows = Collect(wmi, reg).Windows.Value!;

        windows.InstallDate!.Value.UtcDateTime.Date.Should().Be(new DateTime(2025, 3, 12));
        windows.Build.Should().Be(22631);
        windows.Ubr.Should().Be(2314);
        windows.Edition.Should().Be("Windows 11 Pro");
    }

    [Fact]
    public void Activation_LicenseStatus1_IsActivated()
    {
        var (wmi, reg) = TypicalPc();

        Collect(wmi, reg).Windows.Value!.Activation.Should().Be(ActivationState.Activated);
    }

    [Fact]
    public void Activation_NoRow_IsUnknown()
    {
        var (_, reg) = TypicalPc();
        var wmi = new FakeWmiQuery();

        Collect(wmi, reg).Windows.Value!.Activation.Should().Be(ActivationState.Unknown);
    }

    [Fact]
    public void Activation_LicenseStatus0_IsNotActivated()
    {
        var (_, reg) = TypicalPc();
        var wmi = new FakeWmiQuery().Add(Cimv2, "SoftwareLicensingProduct", Row.Of(("LicenseStatus", 0u)));

        Collect(wmi, reg).Windows.Value!.Activation.Should().Be(ActivationState.NotActivated);
    }

    [Fact]
    public void Disk_MediaType4_IsSsd()
    {
        var (wmi, reg) = TypicalPc();

        Collect(wmi, reg).Disks.Value!.Single().MediaType.Should().Be(DiskMediaType.Ssd);
    }

    [Fact]
    public void Disk_BusType17_IsNvme()
    {
        var (wmi, reg) = TypicalPc();

        Collect(wmi, reg).Disks.Value!.Single().IsNvme.Should().BeTrue();
    }

    [Fact]
    public void Monitor_UInt16ArrayName_DecodedAndTrimmed()
    {
        var (wmi, reg) = TypicalPc();

        var display = Collect(wmi, reg).Displays.Value!.Single();

        display.Name.Should().Be("DELL");
        display.DiagonalInches.Should().BeApproximately(27.2, 0.2);
    }

    [Fact]
    public void Battery_Wear_ComputedFromCapacities()
    {
        new BatteryInfo("Battery", 50_000, 40_000).Wear.Should().BeApproximately(0.20, 0.0001);
    }

    [Fact]
    public void Collect_WhenWmiThrows_ReturnsUnavailableSections()
    {
        var (wmi, reg) = TypicalPc();
        wmi.ThrowOnNamespace(Wmi);

        var inventory = Collect(wmi, reg);

        inventory.Displays.Available.Should().BeFalse();
        inventory.Displays.Reason.Should().NotBeNullOrEmpty();
        inventory.Cpu.Available.Should().BeTrue();
        inventory.Volumes.Value.Should().ContainSingle();
    }

    [Fact]
    public void Collect_NoBattery_ReturnsEmptyAvailableList()
    {
        var (wmi, reg) = TypicalPc();

        var batteries = Collect(wmi, reg).Batteries;

        batteries.Available.Should().BeTrue();
        batteries.Value.Should().BeEmpty();
    }

    [Fact]
    public void Collect_LaptopBattery_ReadsCapacities()
    {
        var (wmi, reg) = TypicalPc();
        wmi.Add(Cimv2, "Win32_Battery", Row.Of(("Name", "DELL 7FHHV")))
            .Add(Wmi, "BatteryStaticData", Row.Of(("DesignedCapacity", 56_000u)))
            .Add(Wmi, "BatteryFullChargedCapacity", Row.Of(("FullChargedCapacity", 30_000u)));

        var battery = Collect(wmi, reg).Batteries.Value!.Single();

        battery.Wear.Should().BeApproximately(1 - 30_000.0 / 56_000, 0.0001);
    }

    [Fact]
    public void Memory_UsedComputedFromFree()
    {
        var (wmi, reg) = TypicalPc();

        var memory = Collect(wmi, reg).Memory.Value!;

        memory.TotalBytes.Should().Be(16_000_000L * 1024);
        memory.UsedBytes.Should().Be(10_000_000L * 1024);
        memory.Modules.Should().HaveCount(2);
    }

    [Fact]
    public void Gpu_AdapterRamCapped_ReadsRegistryMemorySize()
    {
        var (wmi, reg) = TypicalPc();
        const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        reg.Set(RegistryHive.LocalMachine, ClassKey + @"\0000", "DriverDesc", "NVIDIA GeForce RTX 3060")
            .Set(RegistryHive.LocalMachine, ClassKey + @"\0000", "HardwareInformation.qwMemorySize", 12_884_901_888L);

        Collect(wmi, reg).Gpus.Value!.Single().MemoryBytes.Should().Be(12_884_901_888L);
    }

    [Fact]
    public void Bios_CimDate_Parsed()
    {
        var (wmi, reg) = TypicalPc();

        Collect(wmi, reg).Bios.Value!.ReleaseDate.Should().Be(new DateOnly(2024, 8, 15));
    }
}
