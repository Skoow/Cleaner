using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.Cleanup;

namespace PCDoc.Modules.Cleanup.Tests;

public class CleanupScannerAndFixerTests
{
    private const long MB = 1024 * 1024;
    private static readonly DateTimeOffset Old = CleanupTests.Now.AddDays(-5);

    private sealed class Runner : IProcessRunner
    {
        public string? Last;

        public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken,
            TimeSpan? timeout = null)
        {
            Last = $"{system32Tool} {arguments}";
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task Scanner_BigSafeCategory_IsFixableInfo_SmallIgnored()
    {
        var fs = new FakeFileSystem().File(@"C:\Windows\Temp\a.tmp", 300 * MB, Old).File(@"C:\Windows\Minidump\x.dmp", 5 * MB, Old);

        var findings = await new CleanupScanner(CleanupTests.Engine(fs)).ScanAsync(CancellationToken.None);

        var f = findings.Single();
        f.Id.Should().Be("cleanup.windows-temp");
        f.Should().Match<Finding>(x => x.Severity == Severity.Info && x.FixAvailable && x.Risk == RiskLevel.Safe && x.Module == "cleanup");
    }

    [Fact]
    public async Task Scanner_BigRecycleBin_IsConditional()
    {
        var engine = CleanupTests.Engine(new FakeFileSystem(), new FakeRecycleBin(3000 * MB, 10));

        var f = (await new CleanupScanner(engine).ScanAsync(CancellationToken.None)).Single();

        f.Id.Should().Be("cleanup.recycle-bin");
        f.Risk.Should().Be(RiskLevel.Conditional);
    }

    [Fact]
    public async Task Fixer_CleansCategory_ReportsFreedBytes()
    {
        var fs = new FakeFileSystem().File(@"C:\Windows\Temp\a.tmp", 300 * MB, Old);
        var engine = CleanupTests.Engine(fs);
        var fixer = new CleanupFixer(engine, new Runner());
        long freed = 0;
        fixer.Freed += (_, bytes) => freed += bytes;
        var finding = CleanupFixer.FindingFor("windows-temp", RiskLevel.Safe);

        fixer.CanFix(finding).Should().BeTrue();
        await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        freed.Should().Be(300 * MB);
        fixer.SupportsUndo.Should().BeFalse();
    }

    [Fact]
    public async Task Fixer_ComponentStore_RunsDismHidden()
    {
        var runner = new Runner();
        var fixer = new CleanupFixer(CleanupTests.Engine(new FakeFileSystem()), runner);

        await fixer.ApplyAsync(CleanupFixer.FindingFor(CleanupEngine.ComponentStoreId, RiskLevel.Safe), CancellationToken.None);

        runner.Last.Should().Be("dism.exe /Online /Cleanup-Image /StartComponentCleanup");
    }

    [Fact]
    public void Fixer_RefusesUnknownCategory_OrUnderstatedRisk()
    {
        var fixer = new CleanupFixer(CleanupTests.Engine(new FakeFileSystem()), new Runner());

        fixer.CanFix(CleanupFixer.FindingFor("C:\\Users", RiskLevel.Safe)).Should().BeFalse();
        fixer.CanFix(CleanupFixer.FindingFor("crash-dumps", RiskLevel.Safe)).Should().BeFalse("les vidages sont 🟠");
        fixer.CanFix(CleanupFixer.FindingFor("crash-dumps", RiskLevel.Conditional)).Should().BeTrue();
    }

    [Fact]
    public void RecycleBin_NeedsStrongConfirmation()
    {
        FixPolicy.NeedsStrongConfirmation(CleanupFixer.FindingFor(CleanupEngine.RecycleBinId, RiskLevel.Conditional)).Should().BeTrue();
        FixPolicy.NeedsStrongConfirmation(CleanupFixer.FindingFor("windows-temp", RiskLevel.Safe)).Should().BeFalse();
    }
}
