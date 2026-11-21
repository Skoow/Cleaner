using System.Collections;
using System.Globalization;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Boot;
using PCDoc.Modules.System.Inventory;

namespace PCDoc.Modules.System.Tests;

public class SystemScannerTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private const long GB = 1_000_000_000;

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }

    private sealed class Inventory(Func<SystemInventory> make) : IInventorySource
    {
        public SystemInventory Collect() => make();
    }

    private sealed class Boots(BootHistory history) : IBootSource
    {
        public BootHistory Read(int maxBoots = 30) => history;
    }

    private sealed class Autoruns(params AutorunEntry[] entries) : IAutorunSource
    {
        public string[] Failed { get; init; } = [];

        public AutorunScanResult CollectAll() => new(entries, Failed);
    }

    private static readonly Win11Readiness AllPass = new(CheckState.Pass, CheckState.Pass, CheckState.Pass,
        CheckState.Pass, CheckState.Pass, CheckState.Pass, CheckState.Pass);

    private static SystemInventory Pc(
        int build = 22631,
        ActivationState activation = ActivationState.Activated,
        VolumeInfo[]? volumes = null,
        BatteryInfo[]? batteries = null,
        Win11Readiness? win11 = null) => new(
        Section<WindowsInfo>.Of(new WindowsInfo(WindowsVersionNamer.Name(build), "Pro", "23H2", build, 1, null, activation)),
        Section<CpuInfo>.Of(new CpuInfo("cpu", 6, 12, 3700)),
        Section<MemoryInfo>.Of(new MemoryInfo(16 * GB, 8 * GB, [])),
        Section<IReadOnlyList<GpuInfo>>.Of([]),
        Section<BoardInfo>.Of(new BoardInfo(null, null)),
        Section<BiosInfo>.Of(new BiosInfo(null, null, null)),
        Section<IReadOnlyList<DiskInfo>>.Of([]),
        Section<IReadOnlyList<VolumeInfo>>.Of(volumes ?? [new VolumeInfo("C:", 500 * GB, 200 * GB, "NTFS")]),
        Section<IReadOnlyList<DisplayInfo>>.Of([]),
        Section<IReadOnlyList<BatteryInfo>>.Of(batteries ?? []),
        Section<Win11Readiness>.Of(win11 ?? AllPass));

    private static BootHistory History(params int[] seconds) => new(
        [.. seconds.Select((s, i) => new BootRecord(Today.AddDays(-i), TimeSpan.FromSeconds(s), TimeSpan.Zero, TimeSpan.Zero, []))]);

    private static AutorunEntry Entry(string name, SignatureStatus status, bool enabled = true, bool microsoft = false,
        AutorunKind kind = AutorunKind.Startup) =>
        new(kind, name, @"HKCU\Run", $@"C:\x\{name}.exe", $@"C:\x\{name}.exe", enabled,
            new SignatureInfo(status, microsoft ? "Microsoft Windows" : null, false), microsoft);

    private static async Task<IReadOnlyList<Finding>> Scan(
        SystemInventory? pc = null, BootHistory? boots = null, AutorunEntry[]? autoruns = null, DateTimeOffset? now = null)
    {
        var sut = new SystemScanner(new Inventory(() => pc ?? Pc()), new Boots(boots ?? History()),
            new Autoruns(autoruns ?? []), new FixedClock(now ?? Today));
        return await sut.ScanAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HealthyPc_NoFindings() => (await Scan()).Should().BeEmpty();

    [Fact]
    public async Task DiskSpace_9Percent_IsWarning()
    {
        var findings = await Scan(Pc(volumes: [new VolumeInfo("C:", 500 * GB, 45 * GB, "NTFS")]));

        findings.Should().ContainSingle().Which.Should().Match<Finding>(f =>
            f.Id == "system.disk-low.c" && f.Severity == Severity.Warning && f.Module == "system");
    }

    [Fact]
    public async Task DiskSpace_4Percent_IsCritical() =>
        (await Scan(Pc(volumes: [new VolumeInfo("C:", 500 * GB, 20 * GB, "NTFS")]))).Single().Severity.Should().Be(Severity.Critical);

    [Fact]
    public async Task DiskSpace_1_5GioOn1To_IsCritical() =>
        (await Scan(Pc(volumes: [new VolumeInfo("D:", 30 * GB, 1_610_612_736, "NTFS")]))).Single().Severity.Should().Be(Severity.Critical);

    [Fact]
    public async Task DiskSpace_11Percent_NoFinding() =>
        (await Scan(Pc(volumes: [new VolumeInfo("C:", 500 * GB, 55 * GB, "NTFS")]))).Should().BeEmpty();

    [Fact]
    public async Task DiskSpace_ZeroSizeVolume_Ignored() =>
        (await Scan(Pc(volumes: [new VolumeInfo("E:", 0, 0, null)]))).Should().BeEmpty();

    [Fact]
    public async Task BootRule_Median61s_IsWarning()
    {
        var finding = (await Scan(boots: History(61, 61, 61, 10, 200))).Single();

        finding.Id.Should().Be("system.boot-slow");
        finding.Severity.Should().Be(Severity.Warning);
    }

    [Fact]
    public async Task BootRule_Median121s_IsCritical() =>
        (await Scan(boots: History(121, 130, 125))).Single().Severity.Should().Be(Severity.Critical);

    [Fact]
    public async Task BootRule_Median60s_NoFinding() => (await Scan(boots: History(60, 60, 60))).Should().BeEmpty();

    [Fact]
    public async Task BootRule_WhenNoHistory_ProducesNothing() => (await Scan(boots: History())).Should().BeEmpty();

    [Fact]
    public async Task BootDegradation_Over5s_IsInfo_LastBootOnly()
    {
        var last = new BootRecord(Today, TimeSpan.FromSeconds(40), TimeSpan.Zero, TimeSpan.Zero,
        [
            new BootDegradation(DegradationKind.Driver, "nvlddmkm.sys", "NVIDIA", TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(6.2)),
            new BootDegradation(DegradationKind.Service, "Spooler", "Spooler", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3)),
        ]);
        var older = new BootRecord(Today.AddDays(-1), TimeSpan.FromSeconds(40), TimeSpan.Zero, TimeSpan.Zero,
            [new BootDegradation(DegradationKind.Application, "old.exe", "old", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(19))]);

        var findings = await Scan(boots: new BootHistory([last, older]));

        findings.Should().ContainSingle().Which.Should().Match<Finding>(f =>
            f.Id == "system.boot-degradation.nvlddmkm.sys" && f.Severity == Severity.Info);
    }

    [Fact]
    public async Task Activation_NotActivated_IsWarning() =>
        (await Scan(Pc(activation: ActivationState.NotActivated))).Single().Id.Should().Be("system.not-activated");

    [Fact]
    public async Task Activation_Unknown_NoFinding() =>
        (await Scan(Pc(activation: ActivationState.Unknown))).Should().BeEmpty();

    [Fact]
    public async Task Win10Eos_AfterEndDate_IsWarning()
    {
        var findings = await Scan(Pc(build: 19045));

        findings.Should().Contain(f => f.Id == "system.win10-eos" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Win10Eos_BeforeEndDate_NoFinding() =>
        (await Scan(Pc(build: 19045), now: new DateTimeOffset(2025, 10, 13, 0, 0, 0, TimeSpan.Zero)))
            .Should().NotContain(f => f.Id == "system.win10-eos");

    [Fact]
    public async Task Win10Eos_Windows11_NoFinding() => (await Scan(Pc(build: 22631))).Should().BeEmpty();

    [Fact]
    public async Task Win11Ready_Win10AllPass_IsInfo() =>
        (await Scan(Pc(build: 19045))).Should().Contain(f => f.Id == "system.win11-ready" && f.Severity == Severity.Info);

    [Fact]
    public async Task Win11Blocked_ListsFailedChecks()
    {
        var readiness = AllPass with { Tpm20 = CheckState.Fail, SecureBoot = CheckState.Fail };

        var finding = (await Scan(Pc(build: 19045, win11: readiness))).Single(f => f.Id == "system.win11-blocked");

        finding.Severity.Should().Be(Severity.Warning);
        var listed = ((IEnumerable)finding.Explanation.Args[0]).Cast<LocalizedText>().Select(t => t.Key);
        listed.Should().Equal("Check.Tpm20", "Check.SecureBoot");
    }

    [Fact]
    public async Task Win11_OnlyUnknown_NoFinding()
    {
        var readiness = AllPass with { CpuSupported = CheckState.Unknown };

        (await Scan(Pc(build: 19045, win11: readiness))).Should().NotContain(f => f.Id.StartsWith("system.win11", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Win11Rule_OnWindows11_NoFinding()
    {
        var readiness = AllPass with { Tpm20 = CheckState.Fail };

        (await Scan(Pc(build: 22631, win11: readiness))).Should().BeEmpty();
    }

    [Fact]
    public async Task BatteryWear_40Percent_IsWarning() =>
        (await Scan(Pc(batteries: [new BatteryInfo("B", 50_000, 30_000)]))).Single().Id.Should().Be("system.battery-wear.0");

    [Fact]
    public async Task BatteryWear_39Percent_NoFinding() =>
        (await Scan(Pc(batteries: [new BatteryInfo("B", 100_000, 61_000)]))).Should().BeEmpty();

    [Fact]
    public async Task Unsigned_EnabledThirdParty_IsWarning()
    {
        var finding = (await Scan(autoruns: [Entry("UpdaterHelper", SignatureStatus.Unsigned)])).Single();

        finding.Id.Should().Be("system.unsigned.startup.updaterhelper");
        finding.Severity.Should().Be(Severity.Warning);
        finding.TechnicalDetails.Should().Contain(@"C:\x\UpdaterHelper.exe");
    }

    [Fact]
    public async Task Invalid_Signature_IsWarning() =>
        (await Scan(autoruns: [Entry("Tampered", SignatureStatus.Invalid, kind: AutorunKind.Service)])).Single().Id
            .Should().Be("system.unsigned.service.tampered");

    [Fact]
    public async Task Unsigned_MicrosoftDisabledEntry_NoFinding() =>
        (await Scan(autoruns: [Entry("X", SignatureStatus.Unsigned, enabled: false), Entry("Y", SignatureStatus.Signed, microsoft: true)]))
            .Should().BeEmpty();

    [Fact]
    public async Task Orphan_IsInfoNotUnsigned()
    {
        var finding = (await Scan(autoruns: [Entry("OldSync", SignatureStatus.FileNotFound)])).Single();

        finding.Id.Should().Be("system.orphan.startup.oldsync");
        finding.Severity.Should().Be(Severity.Info);
    }

    [Fact]
    public async Task NotApplicableSignature_NoFinding() =>
        (await Scan(autoruns: [Entry("COM", SignatureStatus.NotApplicable, kind: AutorunKind.ScheduledTask)])).Should().BeEmpty();

    [Fact]
    public async Task Scan_WhenInventoryThrows_StillReturnsAutorunFindings()
    {
        var sut = new SystemScanner(new Inventory(() => throw new InvalidOperationException("WMI down")),
            new Boots(History()), new Autoruns(Entry("Bad", SignatureStatus.Unsigned)), new FixedClock(Today));

        var act = () => sut.ScanAsync(CancellationToken.None);

        var partial = (await act.Should().ThrowAsync<global::PCDoc.Core.Modules.PartialScanException>()).Which;
        partial.Findings.Should().ContainSingle(f => f.Id.StartsWith("system.unsigned", StringComparison.Ordinal));
        partial.FailedSources.Should().Equal("inventory");
    }

    [Fact]
    public async Task Scan_WhenAutorunSubSourceFails_ReportsPartial()
    {
        var sut = new SystemScanner(new Inventory(() => Pc()), new Boots(History()),
            new Autoruns(Entry("Bad", SignatureStatus.Unsigned)) { Failed = ["tasks"] }, new FixedClock(Today));

        var act = () => sut.ScanAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<global::PCDoc.Core.Modules.PartialScanException>()).Which.FailedSources.Should().Equal("autoruns.tasks");
    }

    [Fact]
    public async Task DiskLow_RendersReadableFrenchAndEnglish()
    {
        var finding = (await Scan(Pc(volumes: [new VolumeInfo("C:", 500 * GB, 9_800_000_000, "NTFS")]))).Single();
        var localizer = new global::PCDoc.Core.Localization.Localizer();
        SystemModule.RegisterResources(localizer);

        localizer.Format(finding.Title).Should().Be("Disque C: presque plein");
        localizer.Format(finding.Explanation).Should().Be(
            "Il reste 9,8 Go libres sur 500 Go (2 %). Windows peut ralentir et les mises à jour échouer.");
        localizer.Language = global::PCDoc.Core.Localization.AppLanguage.English;
        localizer.Format(finding.Explanation).Should().StartWith("Only 9.8 GB free out of 500 GB (2%)");
    }

    [Fact]
    public async Task Win11Blocked_RendersFailedCheckNames()
    {
        var readiness = AllPass with { Tpm20 = CheckState.Fail, SecureBoot = CheckState.Fail };
        var finding = (await Scan(Pc(build: 19045, win11: readiness))).Single(f => f.Id == "system.win11-blocked");
        var localizer = new global::PCDoc.Core.Localization.Localizer();
        SystemModule.RegisterResources(localizer);

        localizer.Format(finding.Explanation).Should().StartWith("Contrôles en échec : TPM 2.0 absent ou désactivé, Secure Boot désactivé.");
    }

    [Fact]
    public async Task AllFindingKeys_ExistInBothLanguages()
    {
        var findings = await Scan(
            Pc(build: 19045, activation: ActivationState.NotActivated, volumes: [new VolumeInfo("C:", 500 * GB, 10 * GB, "NTFS")],
                batteries: [new BatteryInfo("B", 50_000, 10_000)],
                win11: new Win11Readiness(CheckState.Fail, CheckState.Fail, CheckState.Fail, CheckState.Fail, CheckState.Fail, CheckState.Fail, CheckState.Fail)),
            new BootHistory([new BootRecord(Today, TimeSpan.FromSeconds(130), TimeSpan.Zero, TimeSpan.Zero,
                [.. Enum.GetValues<DegradationKind>().Select(k => new BootDegradation(k, k.ToString(), k.ToString(), TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(8)))])]),
            [.. Enum.GetValues<AutorunKind>().SelectMany(k => new[] { Entry("u" + k, SignatureStatus.Unsigned, kind: k), Entry("o" + k, SignatureStatus.FileNotFound, kind: k) })]);
        var withWin11Ready = await Scan(Pc(build: 19045));

        var keys = findings.Concat(withWin11Ready).SelectMany(f => Keys(f.Title).Concat(Keys(f.Explanation))).Distinct().ToList();
        var english = ResourceKeys(CultureInfo.InvariantCulture);
        var french = ResourceKeys(CultureInfo.GetCultureInfo("fr"));

        keys.Should().HaveCountGreaterThan(15);
        english.Should().Contain(keys);
        french.Should().BeEquivalentTo(english);
    }

    private static IEnumerable<string> Keys(LocalizedText text) =>
        new[] { text.Key }.Concat(text.Args.SelectMany(a => a switch
        {
            LocalizedText nested => Keys(nested),
            IEnumerable<LocalizedText> list => list.SelectMany(Keys),
            _ => [],
        }));

    private static HashSet<string> ResourceKeys(CultureInfo culture) =>
        SystemModule.Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
}
