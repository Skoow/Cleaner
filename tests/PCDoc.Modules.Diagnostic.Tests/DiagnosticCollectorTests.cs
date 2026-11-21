using PCDoc.Core.Abstractions;
using PCDoc.Modules.Diagnostic;
using PCDoc.Modules.System.Tests.Fakes;
using static PCDoc.Modules.System.Tests.Fakes.FakeEventLogQuery;

namespace PCDoc.Modules.Diagnostic.Tests;

public class DiagnosticCollectorTests
{
    internal static readonly DateTimeOffset Today = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    internal const string Storage = @"root\Microsoft\Windows\Storage";
    internal const string Cimv2 = @"root\cimv2";

    internal sealed class Clock : IClock
    {
        public DateTimeOffset Now => Today;
    }

    internal static EventRecordData BugCheck(int daysAgo, string param1) =>
        Event(1001, Today.AddDays(-daysAgo), ("param1", param1), ("param2", @"C:\Windows\Minidump\112026-01.dmp")) with
        {
            Provider = DiagnosticCollector.WerProvider,
        };

    internal static EventRecordData AppCrash(int daysAgo, string app, string module = "ntdll.dll") =>
        Event(1000, Today.AddDays(-daysAgo), ("AppName", app), ("ModuleName", module), ("ExceptionCode", "c0000005")) with
        {
            Provider = DiagnosticCollector.AppErrorProvider,
        };

    internal static DiagnosticCollector Make(FakeEventLogQuery? log = null, FakeWmiQuery? wmi = null, FakeRegistryReader? reg = null) =>
        new(log ?? new FakeEventLogQuery(), wmi ?? new FakeWmiQuery(), reg ?? new FakeRegistryReader(), new Clock());

    [Fact]
    public void BugChecks_ParsedWithCodeName_Last30DaysOnly()
    {
        var log = new FakeEventLogQuery(BugCheck(2, "0x0000009f (0x0000000000000003, 0xffffe0012345, 0x0, 0x0)"), BugCheck(45, "0x0000000a (0x0)"));

        var crash = Make(log).Collect().BlueScreens.Value!.Single();

        crash.Code.Should().Be(0x9F);
        crash.CodeName.Should().Be("DRIVER_POWER_STATE_FAILURE");
        crash.Category.Should().Be(BugCheckCategory.Driver);
        crash.DumpFile.Should().EndWith("112026-01.dmp");
    }

    [Fact]
    public void BugCheck_UnknownCode_HasHexName()
    {
        var log = new FakeEventLogQuery(BugCheck(1, "0x00000abc (0x0)"));

        var crash = Make(log).Collect().BlueScreens.Value!.Single();

        crash.CodeName.Should().Be("0x00000ABC");
        crash.Category.Should().Be(BugCheckCategory.Unknown);
    }

    [Fact]
    public void UnexpectedShutdowns_CountKernelPower41WithoutBugcheck()
    {
        var log = new FakeEventLogQuery(
            Event(41, Today.AddDays(-1), ("BugcheckCode", "0")) with { Provider = DiagnosticCollector.KernelPowerProvider },
            Event(41, Today.AddDays(-3), ("BugcheckCode", "0")) with { Provider = DiagnosticCollector.KernelPowerProvider },
            Event(41, Today.AddDays(-4), ("BugcheckCode", "159")) with { Provider = DiagnosticCollector.KernelPowerProvider });

        Make(log).Collect().UnexpectedShutdowns.Value.Should().Be(2);
    }

    [Fact]
    public void AppCrashes_GroupedByApp_WithHangs()
    {
        var log = new FakeEventLogQuery(
            AppCrash(1, "Discord.exe"), AppCrash(2, "Discord.exe"), AppCrash(3, "discord.exe", "nvwgf2umx.dll"), AppCrash(4, "game.exe"),
            Event(1002, Today.AddDays(-1), ("#0", "explorer.exe"), ("#1", "10.0")) with { Provider = DiagnosticCollector.AppHangProvider },
            AppCrash(60, "old.exe"));

        var apps = Make(log).Collect().AppCrashes.Value!;

        apps.Should().HaveCount(3);
        var discord = apps.Single(a => a.App.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase));
        discord.Crashes.Should().Be(3);
        discord.TopModule.Should().Be("ntdll.dll");
        apps.Single(a => a.App == "explorer.exe").Hangs.Should().Be(1);
        apps[0].App.Should().BeEquivalentTo("Discord.exe", "les plus fréquentes d'abord");
    }

    [Fact]
    public void Devices_InError_ExcludeDisabledAndDisconnected()
    {
        var wmi = new FakeWmiQuery().Add(Cimv2, "Win32_PnPEntity",
            Row.Of(("Name", "Realtek Audio"), ("ConfigManagerErrorCode", 10u), ("PNPDeviceID", @"HDAUDIO\FUNC_01")),
            Row.Of(("Name", "Webcam"), ("ConfigManagerErrorCode", 22u), ("PNPDeviceID", "USB\\1")),
            Row.Of(("Name", "Old phone"), ("ConfigManagerErrorCode", 45u), ("PNPDeviceID", "USB\\2")),
            Row.Of(("Name", "Unknown device"), ("ConfigManagerErrorCode", 28u), ("PNPDeviceID", "PCI\\3")));

        var devices = Make(wmi: wmi).Collect().Devices.Value!;

        devices.Select(d => d.Code).Should().Equal(10u, 28u);
    }

    [Fact]
    public void Disks_HealthAndReliabilityCounters()
    {
        var wmi = new FakeWmiQuery()
            .Add(Storage, "MSFT_PhysicalDisk",
                Row.Of(("DeviceId", "0"), ("FriendlyName", "Samsung 980"), ("HealthStatus", (ushort)0), ("MediaType", (ushort)4)),
                Row.Of(("DeviceId", "1"), ("FriendlyName", "WDC HDD"), ("HealthStatus", (ushort)1), ("MediaType", (ushort)3)))
            .Add(Storage, "MSFT_StorageReliabilityCounter",
                Row.Of(("DeviceId", "0"), ("Temperature", (ushort)41), ("Wear", (byte)93), ("ReadErrorsUncorrected", 0ul)),
                Row.Of(("DeviceId", "1"), ("Temperature", (ushort)35), ("Wear", (byte)0), ("ReadErrorsUncorrected", 12ul)));

        var disks = Make(wmi: wmi).Collect().Disks.Value!;

        disks[0].Should().Match<DiskHealth>(d => d.Health == DiskHealthStatus.Healthy && d.WearPercent == 93 && d.TemperatureC == 41);
        disks[1].Should().Match<DiskHealth>(d => d.Health == DiskHealthStatus.Warning && d.UncorrectedReadErrors == 12);
    }

    [Fact]
    public void Disks_ReliabilityUnavailable_StillReportsHealth()
    {
        var wmi = new FakeWmiQuery()
            .Add(Storage, "MSFT_PhysicalDisk", Row.Of(("DeviceId", "0"), ("FriendlyName", "Disk"), ("HealthStatus", (ushort)2)))
            .ThrowOnClass("MSFT_StorageReliabilityCounter");

        var disk = Make(wmi: wmi).Collect().Disks.Value!.Single();

        disk.Health.Should().Be(DiskHealthStatus.Unhealthy);
        disk.WearPercent.Should().BeNull();
    }

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", "x")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", "x")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations")]
    public void PendingReboot_Detected(string key, string value)
    {
        var reg = new FakeRegistryReader().Set(RegistryHive.LocalMachine, key, value, new[] { @"\??\C:\x" });

        Make(reg: reg).Collect().RebootPending.Value.Should().BeTrue();
    }

    [Fact]
    public void PendingReboot_NoneByDefault() => Make().Collect().RebootPending.Value.Should().BeFalse();

    [Fact]
    public void OldDrivers_NonMicrosoftOlderThan5Years()
    {
        var wmi = new FakeWmiQuery().Add(Cimv2, "Win32_PnPSignedDriver",
            Row.Of(("DeviceName", "Old printer"), ("DriverProviderName", "HP"), ("DriverVersion", "1.0"), ("DriverDate", "20180101000000.******+***")),
            Row.Of(("DeviceName", "Inbox"), ("DriverProviderName", "Microsoft"), ("DriverVersion", "10.0"), ("DriverDate", "20060621000000.******+***")),
            Row.Of(("DeviceName", "New GPU"), ("DriverProviderName", "NVIDIA"), ("DriverVersion", "32.0"), ("DriverDate", "20260901000000.******+***")));

        Make(wmi: wmi).Collect().OldDrivers.Value!.Select(d => d.Device).Should().Equal("Old printer");
    }

    [Fact]
    public void WmiDown_SectionsUnavailable_EventsStillRead()
    {
        var wmi = new FakeWmiQuery().ThrowOnNamespace(Cimv2).ThrowOnNamespace(Storage);
        var log = new FakeEventLogQuery(BugCheck(1, "0x0000003b (0x0)"));

        var report = Make(log, wmi).Collect();

        report.Devices.Available.Should().BeFalse();
        report.Disks.Available.Should().BeFalse();
        report.BlueScreens.Value.Should().ContainSingle();
    }
}
