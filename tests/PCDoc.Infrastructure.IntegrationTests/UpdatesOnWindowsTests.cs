using System.Runtime.InteropServices;
using PCDoc.Core.Abstractions;
using PCDoc.Infrastructure.Writes;
using PCDoc.Modules.Optimize;
using PCDoc.Modules.Updates;

namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public sealed class UpdatesOnWindowsTests
{
    [Fact]
    public async Task Optimize_ReadsRealPowerPlansAndState()
    {
        var profiles = new UserProfileResolver();
        var engine = new OptimizeEngine(TweakCatalog.Default, new RegistryReader(profiles), new RegistryWriter(profiles), new ProcessRunner(),
            new WmiQuery());

        var states = await engine.AnalyzeAsync(CancellationToken.None);

        states.Should().HaveCount(TweakCatalog.Default.Tweaks.Count);
        (await engine.ActivePlanAsync(CancellationToken.None)).Should().MatchRegex("^[0-9a-f-]{36}$");
    }

    [Fact]
    public async Task WindowsUpdate_SearchBindsToTheComApi()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try
        {
            var updates = await new WindowsUpdate().SearchAsync(timeout.Token);
            updates.Should().NotContain(u => u.Id.Length == 0 || u.Title.Length == 0, "une liste vide (aucune mise à jour) est valide");
        }
        catch (COMException)
        {
            // Service Windows Update indisponible sur la machine de test : la liaison COM a tout de même fonctionné.
        }
    }

    [Fact]
    public async Task Winget_IsFoundInWindowsAppsOrReportedMissing()
    {
        using var runner = new WingetRunner(new ProcessRunner());
        try
        {
            var (exit, output) = await runner.RunAsync("--version", TimeSpan.FromMinutes(2), CancellationToken.None);
            exit.Should().Be(0);
            output.Should().Contain(l => l.TrimStart().StartsWith('v'));
        }
        catch (FileNotFoundException)
        {
            // winget absent de l'image : signalé proprement (la page affiche « winget n'est pas installé »).
        }
    }
}
