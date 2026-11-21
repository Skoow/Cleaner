using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Infrastructure.Writes;
using PCDoc.Modules.Apps;

namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public sealed class AppsOnWindowsTests
{
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    [Fact]
    public void Launcher_TrustsOnlyProtectedExecutables()
    {
        var launcher = new UninstallLauncher();
        var temp = Path.Combine(Path.GetTempPath(), "pcdoc-fake-uninstall.exe");
        File.WriteAllText(temp, "x");
        try
        {
            launcher.IsTrusted(Path.Combine(Environment.SystemDirectory, "cmd.exe")).Should().BeTrue();
            launcher.IsTrusted(temp).Should().BeFalse("le dossier temporaire est modifiable par l'utilisateur");
            launcher.IsTrusted(@"\\server\share\u.exe").Should().BeFalse();
            launcher.IsTrusted(Path.Combine(ProgramFiles, "..", "Windows", "System32", "cmd.exe")).Should().BeFalse();
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Quarantine_MovesAndRestores_RefusesSharedFolders()
    {
        var quarantine = new Quarantine(new SystemClock());
        var folder = Path.Combine(ProgramFiles, "PCDoc-Test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllText(Path.Combine(folder, "bin", "left.dll"), "x");
        try
        {
            quarantine.IsLeftoverCandidate(Path.Combine(ProgramFiles, "Common Files", "Contoso")).Should().BeFalse();
            quarantine.IsLeftoverCandidate(ProgramFiles).Should().BeFalse();
            quarantine.IsLeftoverCandidate(folder).Should().BeTrue();

            var target = quarantine.PlanFor(folder);
            quarantine.Move(folder, target);
            Directory.Exists(folder).Should().BeFalse();
            File.Exists(Path.Combine(target, "bin", "left.dll")).Should().BeTrue();

            quarantine.Restore(target, folder);
            File.Exists(Path.Combine(folder, "bin", "left.dll")).Should().BeTrue();
            quarantine.PurgeOlderThan(TimeSpan.FromDays(7));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public async Task Inventory_ListsWin32AndStoreApps()
    {
        var profiles = new UserProfileResolver();
        var engine = new AppsEngine(new Win32AppReader(new RegistryReader(profiles)),
            new StoreAppReader(new ProcessRunner(), new AppDisplayNames(), profiles), BloatwareCatalog.Default,
            new UninstallLauncher(), new ProcessRunner(), new Quarantine(new SystemClock()), profiles);

        var report = await engine.AnalyzeAsync(CancellationToken.None);

        report.Apps.Should().Contain(a => a.App.Kind == AppKind.Win32);
        report.Apps.Should().Contain(a => a.App.Kind == AppKind.Store, "Get-AppxPackage doit répondre sur le runner");
        report.Apps.Where(a => a.App.Kind == AppKind.Store && a.App.NonRemovable).Should().OnlyContain(a => a.Risk == RiskLevel.Locked);
    }
}
