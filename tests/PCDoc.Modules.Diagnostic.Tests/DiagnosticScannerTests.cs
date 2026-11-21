using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;
using PCDoc.Modules.Diagnostic;
using PCDoc.Modules.Diagnostic.Repairs;

namespace PCDoc.Modules.Diagnostic.Tests;

public class DiagnosticScannerTests
{
    private static readonly DateTimeOffset Today = DiagnosticCollectorTests.Today;

    private sealed class Source(DiagnosticReport report) : IDiagnosticSource
    {
        public DiagnosticReport Collect() => report;
    }

    internal static DiagnosticReport Report(
        BlueScreen[]? bsod = null, int shutdowns = 0, AppCrashGroup[]? apps = null, DeviceProblem[]? devices = null,
        DiskHealth[]? disks = null, bool reboot = false, OldDriver[]? drivers = null) => new(
        Section<IReadOnlyList<BlueScreen>>.Of(bsod ?? []),
        Section<int>.Of(shutdowns),
        Section<IReadOnlyList<AppCrashGroup>>.Of(apps ?? []),
        Section<IReadOnlyList<DeviceProblem>>.Of(devices ?? []),
        Section<IReadOnlyList<DiskHealth>>.Of(disks ?? []),
        Section<bool>.Of(reboot),
        Section<IReadOnlyList<OldDriver>>.Of(drivers ?? []));

    private static BlueScreen Bsod(uint code = 0x9F) =>
        new(Today, code, BugCheckCodes.Describe(code).Name, BugCheckCodes.Describe(code).Category, "(0x3)", null);

    private static Task<IReadOnlyList<Finding>> Scan(DiagnosticReport report) =>
        new DiagnosticScanner(new Source(report)).ScanAsync(CancellationToken.None);

    [Fact]
    public async Task HealthyPc_NoFindings() => (await Scan(Report())).Should().BeEmpty();

    [Fact]
    public async Task OneBlueScreen_IsWarning_ThreeAreCritical()
    {
        (await Scan(Report([Bsod()]))).Single().Should().Match<Finding>(f => f.Id == "diagnostic.bsod" && f.Severity == Severity.Warning && f.Module == "diagnostic");
        (await Scan(Report([Bsod(), Bsod(0x1A), Bsod()]))).Single().Severity.Should().Be(Severity.Critical);
    }

    [Fact]
    public async Task BlueScreen_ExplanationFollowsCategory()
    {
        var finding = (await Scan(Report([Bsod(0x1A)]))).Single();

        finding.Explanation.Key.Should().Be("Diag.Bsod.Explanation.Memory");
        finding.TechnicalDetails.Should().Contain("MEMORY_MANAGEMENT");
    }

    [Fact]
    public async Task UnexpectedShutdowns_ThreeOrMore_IsInfo()
    {
        (await Scan(Report(shutdowns: 2))).Should().BeEmpty();
        (await Scan(Report(shutdowns: 3))).Single().Should().Match<Finding>(f => f.Id == "diagnostic.shutdowns" && f.Severity == Severity.Info);
    }

    [Fact]
    public async Task AppCrashing3Times_IsWarning()
    {
        var findings = await Scan(Report(apps: [new("Discord.exe", 3, 0, "ntdll.dll", Today), new("game.exe", 1, 1, null, Today)]));

        findings.Single().Id.Should().Be("diagnostic.app-crash.discord.exe");
    }

    [Fact]
    public async Task DeviceInError_IsWarning_WithCodeExplanation()
    {
        var finding = (await Scan(Report(devices: [new("Realtek Audio", 10, "HDAUDIO\\1")]))).Single();

        finding.Severity.Should().Be(Severity.Warning);
        finding.Explanation.Key.Should().Be("Diag.Device.Code.10");
    }

    [Fact]
    public async Task UnknownDeviceCode_UsesGenericExplanation() =>
        (await Scan(Report(devices: [new("X", 99, null)]))).Single().Explanation.Key.Should().Be("Diag.Device.Code.Other");

    [Theory]
    [InlineData(DiskHealthStatus.Warning, null, null, Severity.Warning)]
    [InlineData(DiskHealthStatus.Healthy, 90, null, Severity.Warning)]
    [InlineData(DiskHealthStatus.Unhealthy, null, null, Severity.Critical)]
    [InlineData(DiskHealthStatus.Healthy, null, 5L, Severity.Critical)]
    public async Task Disk_HealthRules(DiskHealthStatus health, int? wear, long? errors, Severity expected) =>
        (await Scan(Report(disks: [new("Disk", health, wear, 40, errors)]))).Single().Severity.Should().Be(expected);

    [Fact]
    public async Task Disk_HealthyLowWear_NoFinding() =>
        (await Scan(Report(disks: [new("Disk", DiskHealthStatus.Healthy, 10, 40, 0)]))).Should().BeEmpty();

    [Fact]
    public async Task RebootPending_IsInfo() =>
        (await Scan(Report(reboot: true))).Single().Id.Should().Be("diagnostic.reboot-pending");

    [Fact]
    public async Task OldDrivers_OneGroupedInfo() =>
        (await Scan(Report(drivers: [new("A", "HP", "1", new DateOnly(2015, 1, 1)), new("B", "Canon", "2", new DateOnly(2016, 1, 1))])))
            .Single().Should().Match<Finding>(f => f.Id == "diagnostic.old-drivers" && f.Severity == Severity.Info && f.TechnicalDetails.Contains("Canon"));

    [Fact]
    public async Task UnavailableSection_ReportsPartialScan()
    {
        var report = Report([Bsod()]) with { Disks = Section<IReadOnlyList<DiskHealth>>.Unavailable("not-available") };

        var act = () => Scan(report);

        var partial = (await act.Should().ThrowAsync<PartialScanException>()).Which;
        partial.Findings.Should().ContainSingle(f => f.Id == "diagnostic.bsod");
        partial.FailedSources.Should().Equal("disks");
    }

    [Fact]
    public async Task AllKeys_ExistInBothLanguages()
    {
        var findings = await Scan(Report(
            [.. Enum.GetValues<BugCheckCategory>().Select(c => Bsod(c switch
            {
                BugCheckCategory.Driver => 0x9F, BugCheckCategory.Memory => 0x1A, BugCheckCategory.Disk => 0x7A,
                BugCheckCategory.Hardware => 0x124, BugCheckCategory.System => 0xEF, _ => 0xABC,
            }))],
            3, [new("a.exe", 5, 0, null, Today)],
            [.. new uint[] { 1, 3, 10, 12, 14, 18, 19, 21, 24, 28, 31, 32, 37, 39, 43, 47, 48, 52, 99 }.Select(c => new DeviceProblem("d", c, null))],
            [new("x", DiskHealthStatus.Unhealthy, 95, 40, 3)], true, [new("A", "HP", "1", new DateOnly(2015, 1, 1))]));
        var keys = findings.SelectMany(f => new[] { f.Title.Key, f.Explanation.Key })
            .Concat(Enum.GetValues<RepairKind>().SelectMany(r => new[] { $"Repair.{r}.Title", $"Repair.{r}.Explanation" }))
            .Distinct().ToList();

        foreach (var culture in new[] { global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.CultureInfo.GetCultureInfo("fr") })
        {
            var set = DiagnosticModule.Resources.GetResourceSet(culture, true, false)!;
            keys.Should().OnlyContain(k => set.GetString(k) != null, culture.Name);
        }
    }
}
