using PCDoc.Core.Abstractions;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.System.Tests;

public class AutorunCollectorTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static IReadOnlyList<AutorunEntry> Collect(
        FakeRegistryReader? reg = null, FakeFileProbe? files = null, FakeSignatureVerifier? signatures = null,
        ScheduledTaskInfo[]? tasks = null, ServiceInfo[]? services = null) =>
        new AutorunCollector(reg ?? new FakeRegistryReader(), files ?? new FakeFileProbe(), new FakeProfileResolver(),
            signatures ?? new FakeSignatureVerifier(), new FakeTaskReader(tasks ?? []), new FakeServiceReader(services ?? [])).Collect();

    [Fact]
    public void Collect_MissingFile_SignatureIsFileNotFound()
    {
        var reg = new FakeRegistryReader().Set(RegistryHive.LocalMachine, Run, "OldSync", @"C:\Gone\sync.exe");

        var entry = Collect(reg).Single();

        entry.Kind.Should().Be(AutorunKind.Startup);
        entry.FilePath.Should().Be(@"C:\Gone\sync.exe");
        entry.Signature.Status.Should().Be(SignatureStatus.FileNotFound);
    }

    [Fact]
    public void Collect_ServicesExcludeDrivers_KindDriverSeparate()
    {
        var services = new[]
        {
            new ServiceInfo("Spooler", "Print Spooler", ServiceStartMode.Automatic, "Running", @"C:\Windows\System32\spoolsv.exe", null, false),
            new ServiceInfo("disk", "Disk Driver", ServiceStartMode.Boot, "Running", @"System32\drivers\disk.sys", null, true),
        };

        var entries = Collect(services: services);

        entries.Should().ContainSingle(e => e.Kind == AutorunKind.Service && e.Name == "Print Spooler");
        entries.Should().ContainSingle(e => e.Kind == AutorunKind.Driver && e.FilePath == @"C:\Windows\System32\drivers\disk.sys");
    }

    [Fact]
    public void Collect_DisabledService_IsNotEnabled()
    {
        var services = new[] { new ServiceInfo("Fax", "Fax", ServiceStartMode.Disabled, "Stopped", @"C:\Windows\system32\fxssvc.exe", null, false) };

        Collect(services: services).Single().Enabled.Should().BeFalse();
    }

    [Fact]
    public void Collect_TaskComAction_HasNoFilePath()
    {
        var tasks = new[]
        {
            new ScheduledTaskInfo(@"\Microsoft\Windows\WindowsUpdate\Scheduled Start", "Scheduled Start", true, "Microsoft",
                ["COM {A6BA00FE-40E8-477C-B713-C64A14F28ADB}"], "Daily", null, null),
        };

        var entry = Collect(tasks: tasks).Single();

        entry.Kind.Should().Be(AutorunKind.ScheduledTask);
        entry.FilePath.Should().BeNull();
        entry.Signature.Status.Should().Be(SignatureStatus.NotApplicable);
    }

    [Fact]
    public void Collect_TaskExecAction_VerifiesExecutable()
    {
        const string Exe = @"C:\Program Files (x86)\Google\Update\GoogleUpdate.exe";
        var tasks = new[] { new ScheduledTaskInfo(@"\GoogleUpdateTaskMachineUA", "GoogleUpdateTaskMachineUA", true, null, [$"\"{Exe}\" /ua"], "Hourly", null, null) };

        var entry = Collect(files: new FakeFileProbe().WithFiles(Exe), signatures: new FakeSignatureVerifier().Signed(Exe, "Google LLC"), tasks: tasks).Single();

        entry.FilePath.Should().Be(Exe);
        entry.Signature.Publisher.Should().Be("Google LLC");
        entry.IsMicrosoft.Should().BeFalse();
    }

    [Fact]
    public void Collect_MicrosoftPublisher_IsMicrosoftTrue()
    {
        const string Exe = @"C:\Windows\system32\SecurityHealthSystray.exe";
        var reg = new FakeRegistryReader().Set(RegistryHive.LocalMachine, Run, "SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe");

        var entry = Collect(reg, new FakeFileProbe().WithFiles(Exe), new FakeSignatureVerifier().Signed(Exe, "Microsoft Windows")).Single();

        entry.IsMicrosoft.Should().BeTrue();
    }
}
