using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Modules.Security;
using PCDoc.Presentation.Security;

namespace PCDoc.Presentation.Tests;

public sealed class SecurityPageViewModelTests
{
    private sealed class Source(int scanExit = 0) : ISecuritySource
    {
        public int Scans { get; private set; }

        public Task<SecurityReport> AnalyzeAsync(CancellationToken cancellationToken)
        {
            var usb = new UsbDrive(new RemovableDrive(@"E:\", "CLE"), [], [@"E:\Photos.lnk"]);
            return Task.FromResult(new SecurityReport(
                [new AntivirusInfo("Windows Defender", true, true, true)],
                new DefenderInfo(true, true, true, 1, 1, true),
                [new Detection("Trojan:Win32/Test", 5, "21/11/2026 10:00", true, @"C:\x.exe")],
                [FirewallFixer.FindingFor("Public"), FeatureFixer.Smb1Finding(), SecurityFindings.RealTimeOff(), UsbFixer.FindingFor(usb)],
                [usb]));
        }

        public Task<int> ScanAsync(int scanType, string? root, Action<string>? onOutput, CancellationToken cancellationToken)
        {
            Scans++;
            onOutput?.Invoke("Scanning...");
            return Task.FromResult(scanExit);
        }
    }

    [Fact]
    public async Task Builds_Tabs_AndPreselectsOnlySafeFixes()
    {
        var vm = new SecurityPageViewModel(new Source(), new Localizer(), fixes: null, () => AppMode.Novice);
        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.Antivirus.Should().ContainSingle();
        vm.Detections.Should().ContainSingle();
        vm.Audit.Select(a => a.Finding.Id).Should().BeEquivalentTo(["security.firewall.Public", "security.smb1", "security.defender.realtime"]);
        vm.Audit.Single(a => a.Finding.Id == "security.firewall.Public").IsSelected.Should().BeTrue();
        vm.Audit.Single(a => a.Finding.Id == "security.smb1").CanSelect.Should().BeFalse("🟠 en mode novice");
        vm.Audit.Single(a => a.Finding.Id == "security.defender.realtime").CanSelect.Should().BeFalse("pas de correction automatique");
        vm.Usb.Should().ContainSingle().Which.CanFix.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_ReportsThreats_ThenRefreshes()
    {
        var source = new Source(scanExit: 2);
        var vm = new SecurityPageViewModel(source, new Localizer(), fixes: null);

        await vm.QuickScanCommand.ExecuteAsync(null);

        source.Scans.Should().Be(1);
        vm.IsScanning.Should().BeFalse();
        vm.ScanStatus.Should().NotBeNullOrEmpty();
        vm.HasLoaded.Should().BeTrue();
    }
}
