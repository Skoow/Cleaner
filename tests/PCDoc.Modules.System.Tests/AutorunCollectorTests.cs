using PCDoc.Core.Abstractions;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.System.Tests;

public class AutorunCollectorTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Compte les vérifications simultanées (mesure de parallélisme indépendante de la charge de la machine).</summary>
    private sealed class SlowVerifier : ISignatureVerifier
    {
        private int _running;

        public int MaxConcurrent { get; private set; }

        public SignatureInfo Verify(string filePath)
        {
            var now = Interlocked.Increment(ref _running);
            lock (this)
            {
                MaxConcurrent = Math.Max(MaxConcurrent, now);
            }

            Thread.Sleep(100);
            Interlocked.Decrement(ref _running);
            return new SignatureInfo(SignatureStatus.Signed, "Vendor " + filePath, false);
        }
    }

    [Fact]
    public void Collect_VerifiesSignaturesInParallel_AndKeepsOrder()
    {
        var services = Enumerable.Range(0, 16)
            .Select(i => new ServiceInfo($"svc{i:00}", $"Service {i:00}", ServiceStartMode.Automatic, "Running", $@"C:\s\svc{i:00}.exe", null, false))
            .ToArray();
        var files = new FakeFileProbe().WithFiles([.. services.Select(s => s.ImagePath!)]);

        var verifier = new SlowVerifier();
        var entries = new AutorunCollector(new FakeRegistryReader(), files, new FakeProfileResolver(), verifier,
            new FakeTaskReader(), new FakeServiceReader(services)).CollectAll().Entries;

        verifier.MaxConcurrent.Should().BeGreaterThan(1, "les signatures sont vérifiées en parallèle");
        entries.Select(e => e.Name).Should().Equal(services.Select(s => s.DisplayName));
        entries.Should().OnlyContain(e => e.Signature.Publisher == "Vendor " + e.FilePath);
    }

    [Fact]
    public void Collect_DriverWithoutPath_IsNotApplicableNotOrphan()
    {
        var services = new[] { new ServiceInfo("vdrv", "Virtual driver", ServiceStartMode.Manual, "Stopped", "", null, true) };

        Collect(services: services).Single().Signature.Status.Should().Be(SignatureStatus.NotApplicable);
    }

    private sealed class ThrowingTasks : IScheduledTaskReader
    {
        public IReadOnlyList<ScheduledTaskInfo> ReadAll() => throw new InvalidOperationException("Task Scheduler service disabled");
    }

    [Fact]
    public void Collect_WhenTaskReaderThrows_KeepsOtherSources()
    {
        var reg = new FakeRegistryReader().Set(RegistryHive.LocalMachine, Run, "OldSync", @"C:\Gone\sync.exe");
        var services = new[] { new ServiceInfo("Spooler", "Print Spooler", ServiceStartMode.Automatic, "Running", @"C:\Windows\System32\spoolsv.exe", null, false) };

        var result = new AutorunCollector(reg, new FakeFileProbe(), new FakeProfileResolver(), new FakeSignatureVerifier(),
            new ThrowingTasks(), new FakeServiceReader(services)).CollectAll();

        result.Entries.Should().HaveCount(2);
        result.FailedSources.Should().Equal("tasks");
    }

    private static IReadOnlyList<AutorunEntry> Collect(
        FakeRegistryReader? reg = null, FakeFileProbe? files = null, FakeSignatureVerifier? signatures = null,
        ScheduledTaskInfo[]? tasks = null, ServiceInfo[]? services = null) =>
        new AutorunCollector(reg ?? new FakeRegistryReader(), files ?? new FakeFileProbe(), new FakeProfileResolver(),
            signatures ?? new FakeSignatureVerifier(), new FakeTaskReader(tasks ?? []), new FakeServiceReader(services ?? [])).CollectAll().Entries;

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
    public void Collect_PropagatesServiceNameAndStartupTarget()
    {
        var reg = new FakeRegistryReader().Set(RegistryHive.TargetUser, Run, "Discord", @"C:\d.exe");
        var services = new[] { new ServiceInfo("Spooler", "Print Spooler", ServiceStartMode.Automatic, "Running", @"C:\s.exe", null, false) };

        var entries = Collect(reg, services: services);

        entries.Single(e => e.Kind == AutorunKind.Service).ServiceName.Should().Be("Spooler");
        entries.Single(e => e.Kind == AutorunKind.Startup).Startup.Should().Be(new StartupToggleTarget(RegistryHive.TargetUser, "Run", "Discord"));
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
