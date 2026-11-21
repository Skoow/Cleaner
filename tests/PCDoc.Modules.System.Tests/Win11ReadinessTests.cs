using PCDoc.Core.Abstractions;
using PCDoc.Modules.System.Inventory;
using PCDoc.Modules.System.Tests.Fakes;
using static PCDoc.Modules.System.Tests.InventoryCollectorTests;

namespace PCDoc.Modules.System.Tests;

public class Win11ReadinessTests
{
    private static Win11Readiness Readiness(FakeWmiQuery wmi, FakeRegistryReader reg) =>
        new InventoryCollector(wmi, reg, Win11CpuList.Default).Collect().Win11.Value!;

    [Fact]
    public void Win11_TypicalModernPc_AllPass()
    {
        var (wmi, reg) = TypicalPc(19045);

        Readiness(wmi, reg).AllPass.Should().BeTrue();
    }

    [Fact]
    public void Win11_Tpm12_FailsTpmCheck()
    {
        var (wmi, reg) = TypicalPc(19045);
        wmi = WithTpm(wmi, "1.2, 2, 3");

        Readiness(wmi, reg).Tpm20.Should().Be(CheckState.Fail);
    }

    [Fact]
    public void Win11_NoTpmRow_Fails()
    {
        var (wmi, reg) = TypicalPc(19045);
        wmi = WithTpm(wmi, null);

        Readiness(wmi, reg).Tpm20.Should().Be(CheckState.Fail);
    }

    [Fact]
    public void Win11_TpmAccessDenied_IsUnknown()
    {
        var (wmi, reg) = TypicalPc(19045);
        wmi.ThrowOnNamespace(Tpm, accessDenied: true);

        Readiness(wmi, reg).Tpm20.Should().Be(CheckState.Unknown);
    }

    [Fact]
    public void Win11_SecureBootKeyMissing_FailsUefiAndSecureBoot()
    {
        var (wmi, _) = TypicalPc(19045);
        var reg = new FakeRegistryReader()
            .Set(RegistryHive.LocalMachine, CurrentVersion, "CurrentBuild", "19045");

        var r = Readiness(wmi, reg);

        r.Uefi.Should().Be(CheckState.Fail);
        r.SecureBoot.Should().Be(CheckState.Fail);
    }

    [Fact]
    public void Win11_SecureBootDisabled_FailsOnlySecureBoot()
    {
        var (wmi, reg) = TypicalPc(19045);
        reg.Set(RegistryHive.LocalMachine, SecureBootState, "UEFISecureBootEnabled", 0);

        var r = Readiness(wmi, reg);

        r.Uefi.Should().Be(CheckState.Pass);
        r.SecureBoot.Should().Be(CheckState.Fail);
    }

    [Fact]
    public void Win11_MbrSystemDisk_FailsGpt()
    {
        var (wmi, reg) = TypicalPc(19045);
        wmi = Replace(wmi, Storage, "MSFT_Disk", Row.Of(("Number", 0u), ("PartitionStyle", (ushort)1), ("Size", 500_107_862_016ul)));

        Readiness(wmi, reg).SystemDiskGpt.Should().Be(CheckState.Fail);
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz", CheckState.Pass)]
    [InlineData("Intel(R) Core(TM) i5-7400 CPU @ 3.00GHz", CheckState.Fail)]
    [InlineData("Intel(R) Core(TM) i7-1165G7 @ 2.80GHz", CheckState.Pass)]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700K", CheckState.Pass)]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H", CheckState.Pass)]
    [InlineData("Intel(R) Core(TM)2 Duo CPU E8400 @ 3.00GHz", CheckState.Fail)]
    [InlineData("AMD Ryzen 5 3600 6-Core Processor", CheckState.Pass)]
    [InlineData("AMD Ryzen 7 2700X Eight-Core Processor", CheckState.Pass)]
    [InlineData("AMD Ryzen 7 1700 Eight-Core Processor", CheckState.Fail)]
    [InlineData("AMD Ryzen 5 2400G with Radeon Vega Graphics", CheckState.Fail)]
    [InlineData("Snapdragon(R) X Elite - X1E78100 - Qualcomm(R) Oryon(TM) CPU", CheckState.Pass)]
    [InlineData("QEMU Virtual CPU version 2.5+", CheckState.Unknown)]
    public void Win11_CpuList_Classifies(string cpu, CheckState expected) =>
        Win11CpuList.Default.Check(cpu).Should().Be(expected);

    [Fact]
    public void Win11_CpuListFile_ParsesAndHasPatterns()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Definitions", "win11-supported-cpus.json");

        var list = Win11CpuList.Load(File.ReadAllText(path));

        list.Version.Should().BeGreaterThan(0);
        list.SupportedCount.Should().BeGreaterThan(5);
        list.UnsupportedCount.Should().BeGreaterThan(3);
    }

    [Fact]
    public void Win11_LowRam_FailsRam()
    {
        var (wmi, reg) = TypicalPc(19045);
        wmi = Replace(wmi, InventoryCollectorTests.Cimv2, "Win32_PhysicalMemory", Row.Of(("Capacity", 2_147_483_648ul)));

        Readiness(wmi, reg).Ram4GB.Should().Be(CheckState.Fail);
    }

    private static FakeWmiQuery WithTpm(FakeWmiQuery source, string? spec) =>
        spec is null
            ? Replace(source, Tpm, "Win32_Tpm")
            : Replace(source, Tpm, "Win32_Tpm", Row.Of(("SpecVersion", spec), ("IsEnabled_InitialValue", true)));

    private static FakeWmiQuery Replace(FakeWmiQuery source, string ns, string cls, params Dictionary<string, object?>[] rows) =>
        source.Replace(ns, cls, rows);
}
