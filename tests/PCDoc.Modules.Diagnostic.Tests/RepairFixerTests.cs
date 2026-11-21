using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.Diagnostic.Repairs;

namespace PCDoc.Modules.Diagnostic.Tests;

public class RepairFixerTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string Tool, string Args)> Calls { get; } = [];

        public int ExitCode { get; set; }

        public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken)
        {
            Calls.Add((system32Tool, arguments));
            onOutput?.Invoke("Vérification 100 % effectuée.");
            return Task.FromResult(ExitCode);
        }
    }

    [Theory]
    [InlineData(RepairKind.FlushDns, "ipconfig.exe", "/flushdns")]
    [InlineData(RepairKind.SystemFiles, "sfc.exe", "/scannow")]
    [InlineData(RepairKind.WindowsImage, "dism.exe", "/Online /Cleanup-Image /RestoreHealth")]
    public async Task Apply_RunsTheFixedCommandHidden(RepairKind kind, string tool, string args)
    {
        var runner = new FakeRunner();
        var fixer = new RepairFixer(runner);
        var lines = new List<string>();
        fixer.Output += (_, line) => lines.Add(line);
        var finding = RepairFixer.FindingFor(kind);

        fixer.CanFix(finding).Should().BeTrue();
        await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        runner.Calls.Should().Equal((tool, args));
        lines.Should().ContainSingle();
    }

    [Fact]
    public async Task FailingExitCode_Throws()
    {
        var fixer = new RepairFixer(new FakeRunner { ExitCode = 2 });

        var act = () => fixer.ApplyAsync(RepairFixer.FindingFor(RepairKind.SystemFiles), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void CraftedFixData_IsRejected()
    {
        var fixer = new RepairFixer(new FakeRunner());
        var crafted = RepairFixer.FindingFor(RepairKind.FlushDns) with { FixData = "{\"Repair\":\"cmd.exe /c del\"}" };

        fixer.CanFix(crafted).Should().BeFalse();
    }

    [Fact]
    public void Repairs_AreSafe_NotUndoable()
    {
        var finding = RepairFixer.FindingFor(RepairKind.WindowsImage);

        finding.Risk.Should().Be(RiskLevel.Safe);
        finding.Reversible.Should().BeFalse();
        new RepairFixer(new FakeRunner()).SupportsUndo.Should().BeFalse();
        FixPolicy.NeedsStrongConfirmation(finding).Should().BeFalse("réparation sans risque : pas de confirmation renforcée");
    }
}
