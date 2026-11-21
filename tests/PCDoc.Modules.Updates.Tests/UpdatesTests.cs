using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Updates.Tests;

public class UpdatesTests
{
    private static readonly string[] FrenchOutput =
    [
        "   - \r   \\ \r",
        "Nom                                Id                         Version      Disponible   Source",
        "-----------------------------------------------------------------------------------------------",
        "Mozilla Firefox (x64 fr)           Mozilla.Firefox            131.0        132.0.1      winget",
        "7-Zip 23.01 (x64)                  7zip.7zip                  23.01        24.08        winget",
        "2 mises à niveau disponibles.",
        "",
        "Les packages suivants ont une mise à niveau disponible, mais nécessitent un ciblage explicite :",
        "Nom      Id          Version  Disponible Source",
        "-----------------------------------------------",
        "Pinned   Pin.Ned     1.0      2.0        winget",
    ];

    private sealed class FakeWindowsUpdate : IWindowsUpdate
    {
        public List<string> Installed { get; } = [];

        public Task<IReadOnlyList<WindowsUpdateItem>> SearchAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WindowsUpdateItem>>(
        [
            new("aaa", "Cumulative update KB5040001", false, false, 1000, "5040001"),
            new("bbb", "Intel - Display - 31.0", true, false, 500),
        ]);

        public Task<WindowsUpdateResult> InstallAsync(IReadOnlyCollection<string> updateIds, CancellationToken cancellationToken)
        {
            Installed.AddRange(updateIds);
            return Task.FromResult(new WindowsUpdateResult(updateIds.Count, 0, RebootRequired: true));
        }
    }

    private sealed class FakeWinget(int exit = 0) : IWingetRunner
    {
        public List<string> Calls { get; } = [];

        public bool Missing { get; init; }

        public Task<(int ExitCode, IReadOnlyList<string> Output)> RunAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (Missing)
            {
                throw new FileNotFoundException("winget");
            }

            Calls.Add(arguments);
            return Task.FromResult<(int, IReadOnlyList<string>)>(arguments.StartsWith("upgrade --accept", StringComparison.Ordinal) ? (0, FrenchOutput) : (exit, ["Done"]));
        }
    }

    [Fact]
    public void Parser_ReadsFirstTableOnly_WithTranslatedHeaders()
    {
        var upgrades = Winget.ParseUpgrades(FrenchOutput);

        upgrades.Should().Equal(
            new WingetUpgrade("Mozilla Firefox (x64 fr)", "Mozilla.Firefox", "131.0", "132.0.1"),
            new WingetUpgrade("7-Zip 23.01 (x64)", "7zip.7zip", "23.01", "24.08"));
        Winget.ParseUpgrades(["No installed package found matching input criteria."]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Mozilla.Firefox", true)]
    [InlineData("Notepad++.Notepad++", true)]
    [InlineData("x --override \"evil\"", false)]
    [InlineData("a&b", false)]
    public void Ids_AreValidated(string id, bool valid)
    {
        Winget.IsValidId(id).Should().Be(valid);
        var act = () => Winget.UpgradeArguments(id);
        if (valid)
        {
            act().Should().Be($"upgrade --id {id} --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity");
        }
        else
        {
            act.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public async Task Fixer_InstallsOnlyWhatTheLastSearchFound()
    {
        var wu = new FakeWindowsUpdate();
        var winget = new FakeWinget();
        var engine = new UpdatesEngine(wu, winget);
        var fixer = new UpdateFixer(engine);
        var firefox = UpdateFixer.ForUpgrade(new WingetUpgrade("Firefox", "Mozilla.Firefox", "1", "2"));
        fixer.CanFix(firefox).Should().BeFalse("pas encore de recherche");

        var report = await engine.AnalyzeAsync(includeWindows: true, CancellationToken.None);
        var driver = UpdateFixer.ForWindows(report.Windows[1]);

        fixer.CanFix(firefox).Should().BeTrue();
        fixer.CanFix(UpdateFixer.ForUpgrade(new WingetUpgrade("Evil", "Evil.Package", "1", "2"))).Should().BeFalse();
        driver.Risk.Should().Be(RiskLevel.Conditional);
        fixer.CanFix(driver with { Risk = RiskLevel.Safe }).Should().BeFalse("niveau minimisé");
        fixer.CanFix(UpdateFixer.ForInstall(new WingetPackage("Mozilla.Firefox", "Firefox", "browser"))).Should().BeTrue();
        fixer.CanFix(UpdateFixer.ForInstall(new WingetPackage("Not.InCatalog", "X", "tools"))).Should().BeFalse();

        await fixer.ApplyAsync(firefox, CancellationToken.None);
        await fixer.ApplyAsync(UpdateFixer.ForWindows(report.Windows[0]), CancellationToken.None);

        winget.Calls[^1].Should().StartWith("upgrade --id Mozilla.Firefox --exact --silent");
        wu.Installed.Should().Equal("aaa");
        engine.RebootRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Failures_AreReported()
    {
        var engine = new UpdatesEngine(new FakeWindowsUpdate(), new FakeWinget(exit: 1));
        await engine.AnalyzeAsync(includeWindows: false, CancellationToken.None);

        var act = () => new UpdateFixer(engine).ApplyAsync(UpdateFixer.ForUpgrade(new WingetUpgrade("7-Zip", "7zip.7zip", "1", "2")), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await new UpdatesEngine(new FakeWindowsUpdate(), new FakeWinget { Missing = true }).AnalyzeAsync(false, CancellationToken.None))
            .WingetMissing.Should().BeTrue();
    }

    [Fact]
    public async Task Scanner_ReportsSoftwareUpgrades_AndCatalogIsValid()
    {
        var findings = await new UpdatesScanner(new UpdatesEngine(new FakeWindowsUpdate(), new FakeWinget())).ScanAsync(CancellationToken.None);

        findings.Select(f => f.Id).Should().Equal("updates.upgrade.Mozilla.Firefox", "updates.upgrade.7zip.7zip");
        Winget.DefaultCatalog.Should().HaveCountGreaterThan(10).And.OnlyHaveUniqueItems(p => p.Id);
    }
}
