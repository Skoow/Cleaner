using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Modules.Apps;
using PCDoc.Presentation.Apps;

namespace PCDoc.Presentation.Tests;

public sealed class AppsPageViewModelTests
{
    private sealed class Source(AppsReport report) : IAppsSource
    {
        public Task<AppsReport> AnalyzeAsync(CancellationToken cancellationToken) => Task.FromResult(report);
    }

    private static AppItem Item(string name, RiskLevel risk, BloatReason? reason = null, UninstallMethod? method = UninstallMethod.Msi,
        AppKind kind = AppKind.Win32) =>
        new(new InstalledApp($"win32:64:machine:{name}", kind, AppScope.Machine, name, "Contoso", "1.0", 300L << 20, new DateOnly(2025, 1, 2)),
            risk, reason is { } r ? new BloatEntry(kind, name, risk, r) : null, method is { } m ? new UninstallPlan(m) : null);

    private static async Task<AppsPageViewModel> Make(AppMode mode = AppMode.Novice)
    {
        var report = new AppsReport(
        [
            Item("Viewer", RiskLevel.Safe),
            Item("Visual C++", RiskLevel.Discouraged, BloatReason.Keep),
            Item("Weather", RiskLevel.Safe, BloatReason.Unused, UninstallMethod.Store, AppKind.Store),
            Item("Phone Link", RiskLevel.Conditional, BloatReason.Unused, UninstallMethod.Store, AppKind.Store),
            Item("Chat", RiskLevel.Safe, method: null),
        ], [@"C:\Program Files\Old"]);
        var vm = new AppsPageViewModel(new Source(report), new Localizer(), fixes: null, () => mode);
        await vm.AnalyzeCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Lists_FiltersAndPreselectsOnlySafeBloatware()
    {
        var vm = await Make();

        vm.Installed.Should().HaveCount(5);
        vm.Preinstalled.Select(r => (r.Name, r.IsSelected)).Should().Equal(("Weather", true), ("Phone Link", false));
        vm.Leftovers.Should().ContainSingle().Which.Finding.FixData.Should().Contain("Old");
        vm.HasLeftovers.Should().BeTrue();

        vm.SearchText = "view";
        vm.Installed.Select(r => r.Name).Should().Equal("Viewer");
    }

    [Theory]
    [InlineData(AppMode.Novice, false)]
    [InlineData(AppMode.Expert, true)]
    public async Task UninstallButton_FollowsModeAndSupport(AppMode mode, bool discouragedOffered)
    {
        var vm = await Make(mode);

        vm.Installed.Single(r => r.Name == "Viewer").CanUninstall.Should().BeTrue();
        vm.Installed.Single(r => r.Name == "Visual C++").CanUninstall.Should().Be(discouragedOffered);
        vm.Installed.Single(r => r.Name == "Chat").CanUninstall.Should().BeFalse("aucune méthode de désinstallation");
    }

    [Fact]
    public async Task Unticking_Everything_SurvivesSearchAndRebuild()
    {
        var vm = await Make();
        foreach (var row in vm.Preinstalled)
        {
            row.IsSelected = false;
        }

        vm.SearchText = "w";
        vm.SearchText = string.Empty;

        vm.Preinstalled.Should().OnlyContain(r => !r.IsSelected);
    }
}
