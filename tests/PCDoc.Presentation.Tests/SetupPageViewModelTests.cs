using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Modules.Apps;
using PCDoc.Modules.Cleanup;
using PCDoc.Modules.Optimize;
using PCDoc.Modules.Security;
using PCDoc.Modules.Updates;
using PCDoc.Presentation.Setup;

namespace PCDoc.Presentation.Tests;

public sealed class SetupPageViewModelTests
{
    private sealed class Tweaks(TweakCatalog catalog) : IOptimizeSource
    {
        public Task<IReadOnlyList<TweakState>> AnalyzeAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TweakState>>([.. catalog.Tweaks.Select(t => new TweakState(t, t.Risk, false, true))]);
    }

    private sealed class Security : ISecuritySource
    {
        public Task<SecurityReport> AnalyzeAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("WMI down");

        public Task<int> ScanAsync(int scanType, string? root, Action<string>? onOutput, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class Apps : IAppsSource
    {
        public Task<AppsReport> AnalyzeAsync(CancellationToken cancellationToken) => Task.FromResult(new AppsReport(
        [
            new AppItem(new InstalledApp("store:Microsoft.BingWeather_8w", AppKind.Store, AppScope.User, "Météo"), RiskLevel.Safe,
                new BloatEntry(AppKind.Store, "Microsoft.BingWeather", RiskLevel.Safe, BloatReason.Unused), new UninstallPlan(UninstallMethod.Store)),
            new AppItem(new InstalledApp("win32:64:machine:x", AppKind.Win32, AppScope.Machine, "Viewer"), RiskLevel.Safe, null, new UninstallPlan(UninstallMethod.Msi)),
        ], []));
    }

    private sealed class Updates : IUpdatesSource
    {
        public IReadOnlyList<WingetPackage> Catalog { get; } = [new("VideoLAN.VLC", "VLC", "media")];

        public Task<UpdatesReport> AnalyzeAsync(bool includeWindows, CancellationToken cancellationToken) => Task.FromResult(new UpdatesReport(
            [new WindowsUpdateItem("a", "KB1", false, false, 1), new WindowsUpdateItem("d", "Driver", true, false, 1)],
            [new WingetUpgrade("Firefox", "Mozilla.Firefox", "1", "2")], false, false));
    }

    private sealed class Cleanup : ICleanupSource
    {
        public CleanupReport Analyze() => new([new CategoryResult("windows-temp", RiskLevel.Safe, 10, 1), new CategoryResult("chrome-cache", RiskLevel.Safe, 0, 0)], 0, 0);

        public IReadOnlyList<FileEntry> BigFiles() => [];
    }

    [Fact]
    public async Task Builds_StepsInOrder_OnlyOfferedFixes_AndSurvivesAFailingModule()
    {
        var vm = new SetupPageViewModel(new SetupSources(new Tweaks(OptimizeModule.Privacy), new Security(), new Tweaks(TweakCatalog.Default), new Apps(),
            new Updates(), new Cleanup()), new Localizer(), fixes: null, () => AppMode.Novice);

        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.Steps.Select(s => s.Id).Should().Equal("cleanup", "bloatware", "antivirus", "security", "privacy", "optimize", "windows", "software");
        vm.Steps.Single(s => s.Id == "privacy").Findings.Should().OnlyContain(f => f.Risk == RiskLevel.Safe && f.Module == "privacy").And.NotBeEmpty();
        vm.Steps.Single(s => s.Id == "security").IsSelected.Should().BeFalse("module en échec : étape vide");
        vm.Steps.Single(s => s.Id == "antivirus").Findings.Select(f => f.Id).Should().Equal("security.scan.defenderquick", "security.scan.adwcleaner");
        vm.Steps.Single(s => s.Id == "bloatware").Findings.Should().ContainSingle();
        vm.Steps.Single(s => s.Id == "windows").Findings.Should().ContainSingle("pilote 🟠 exclu en mode novice");
        vm.Steps.Single(s => s.Id == "cleanup").Findings.Should().ContainSingle();
        vm.Steps.Single(s => s.Id == "optimize").Findings.Should().OnlyContain(f => f.Id != "optimize.power-high", "profil Bureautique");

        vm.SelectProfileCommand.Execute("gaming");
        await Task.Delay(50);
        vm.Profile.Should().Be("gaming");
    }

    [Fact]
    public void Privacy_Catalog_HasTwoProfiles_AndNoLockedItems()
    {
        OptimizeModule.Privacy.Tweaks.Should().HaveCountGreaterThan(10)
            .And.OnlyContain(t => t.Module == "privacy" && t.TextPrefix == "Privacy" && t.Profiles.Contains("strict"));
        OptimizeModule.Privacy.Tweaks.Where(t => t.Profiles.Contains("recommended")).Should().OnlyContain(t => t.Risk == RiskLevel.Safe);
    }
}
