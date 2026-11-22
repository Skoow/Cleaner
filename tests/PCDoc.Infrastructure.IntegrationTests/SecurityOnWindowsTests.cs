using PCDoc.Core.Abstractions;
using PCDoc.Infrastructure.Writes;
using PCDoc.Modules.Optimize;
using PCDoc.Modules.Security;

namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public sealed class SecurityOnWindowsTests
{
    [Fact]
    public async Task Analyze_RunsAgainstTheRealSystem()
    {
        var profiles = new UserProfileResolver();
        var registry = new RegistryReader(profiles);
        var writer = new RegistryWriter(profiles);
        var wmi = new WmiQuery();
        var runner = new ProcessRunner();
        var engine = new SecurityEngine(wmi, registry, writer, runner, new DefenderCommand(), new FileProbe(profiles), new RemovableDrives(),
            new OptimizeEngine(SecurityModule.Settings, registry, writer, runner, wmi), Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        var report = await engine.AnalyzeAsync(CancellationToken.None);

        report.Findings.Should().NotContain(f => f.Id == "security.userinit", "le runner a la valeur d'origine de Windows");
        report.Findings.Should().NotContain(f => f.Id == "security.winlogon-shell");
        engine.IsExpectedUserinit(engine.CurrentUserinit()!).Should().BeTrue();
    }

    [Fact]
    public void RemovableDrives_RefuseFixedDisks()
    {
        var drives = new RemovableDrives();

        drives.IsRemovableRoot(@"C:\").Should().BeFalse();
        var act = () => drives.SetAttributes(@"C:\Windows", FileAttributes.Normal);
        act.Should().Throw<InvalidOperationException>();
    }
}
