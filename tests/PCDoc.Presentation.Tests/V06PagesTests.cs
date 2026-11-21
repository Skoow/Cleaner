using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Modules.Optimize;
using PCDoc.Modules.Updates;
using PCDoc.Presentation.Optimize;
using PCDoc.Presentation.Updates;

namespace PCDoc.Presentation.Tests;

public sealed class V06PagesTests
{
    private sealed class UpdatesSource : IUpdatesSource
    {
        public IReadOnlyList<WingetPackage> Catalog { get; } = [new("VideoLAN.VLC", "VLC", "media")];

        public Task<UpdatesReport> AnalyzeAsync(bool includeWindows, CancellationToken cancellationToken) => Task.FromResult(new UpdatesReport(
            [new WindowsUpdateItem("a", "Security update", false, false, 10 << 20, "5040001"), new WindowsUpdateItem("b", "Driver", true, false, 0)],
            [new WingetUpgrade("Firefox", "Mozilla.Firefox", "1", "2")], false, false));
    }

    private sealed class OptimizeSource : IOptimizeSource
    {
        public Task<IReadOnlyList<TweakState>> AnalyzeAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TweakState>>(
            [.. TweakCatalog.Default.Tweaks.Select(t => new TweakState(t, t.Risk, Applied: t.Id == "power-balanced", Available: true))]);
    }

    [Fact]
    public async Task Updates_PreselectsSafeWindowsAndSoftware_NotDriversOrCatalog()
    {
        var vm = new UpdatesPageViewModel(new UpdatesSource(), new Localizer(), fixes: null, () => AppMode.Novice, () => true);
        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.Windows.Select(r => (r.IsSelected, r.CanSelect)).Should().Equal((true, true), (false, false));
        vm.Software.Should().ContainSingle().Which.IsSelected.Should().BeTrue();
        vm.Catalog.Should().ContainSingle().Which.IsSelected.Should().BeFalse();
        vm.RebootRequired.Should().BeTrue();
        vm.WindowsStatus.Should().BeNull();
    }

    [Fact]
    public async Task Optimize_ProfileSelectsItsTweaks_KeepsChoicesOnRebuild()
    {
        var vm = new OptimizePageViewModel(new OptimizeSource(), new Localizer(), fixes: null, () => AppMode.Novice);
        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.Tweaks.Where(t => t.IsSelected).Select(t => t.State.Tweak.Id).Should().BeEquivalentTo(["game-dvr-off", "startup-delay-off"]);
        vm.ApplyProfileCommand.Execute("gaming");
        vm.Tweaks.Where(t => t.IsSelected).Select(t => t.State.Tweak.Id).Should().BeEquivalentTo(
            ["game-dvr-off", "game-mode", "startup-delay-off", "menu-delay"], "power-high est 🟠 sur un portable en mode novice");
        vm.Tweaks.Single(t => t.State.Tweak.Id == "power-balanced").Finding.FixAvailable.Should().BeFalse("déjà appliqué");
        vm.Profiles.Select(p => p.Id).Should().Equal("gaming", "office", "battery");
    }
}
