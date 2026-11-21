using PCDoc.Core.Abstractions;
using PCDoc.Core.Fixing;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Modules.Diagnostic;
using PCDoc.Modules.Diagnostic.Repairs;
using PCDoc.Presentation.Diagnostic;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Tests;

public sealed class DiagnosticPageViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcdoc-diag-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Day = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private sealed class Source(DiagnosticReport report) : IDiagnosticSource
    {
        public DiagnosticReport Collect() => report;
    }

    private sealed class Runner : IProcessRunner
    {
        public string? Last;

        public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken)
        {
            Last = system32Tool;
            onOutput?.Invoke("Vérification 100 % effectuée.");
            return Task.FromResult(0);
        }
    }

    private static DiagnosticReport Report() => new(
        Section<IReadOnlyList<BlueScreen>>.Of([new BlueScreen(Day, 0x9F, "DRIVER_POWER_STATE_FAILURE", BugCheckCategory.Driver, "(0x3)", null)]),
        Section<int>.Of(1),
        Section<IReadOnlyList<AppCrashGroup>>.Of([new AppCrashGroup("Discord.exe", 3, 1, "ntdll.dll", Day)]),
        Section<IReadOnlyList<DeviceProblem>>.Of([new DeviceProblem("Realtek Audio", 10, "HDAUDIO")]),
        Section<IReadOnlyList<DiskHealth>>.Unavailable("access-denied"),
        Section<bool>.Of(true),
        Section<IReadOnlyList<OldDriver>>.Of([]));

    private (DiagnosticPageViewModel Vm, Runner Runner, FixConfirmationViewModel Fixes) Make()
    {
        var loc = new Localizer();
        loc.Register(CoreResources.Manager);
        DiagnosticModule.RegisterResources(loc);
        var runner = new Runner();
        var repair = new RepairFixer(runner);
        var pipeline = new FixPipeline([repair], new FixConfirmationViewModelTests.Restore(RestorePointOutcome.Created),
            new UndoJournal(Path.Combine(_dir, "u.jsonl"), new FixConfirmationViewModelTests.Clock()));
        var fixes = new FixConfirmationViewModel(pipeline, loc, () => AppMode.Novice);
        return (new DiagnosticPageViewModel(new Source(Report()), loc, repair, fixes), runner, fixes);
    }

    [Fact]
    public async Task Refresh_BuildsTranslatedRows()
    {
        var (vm, _, _) = Make();

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.BlueScreens.Single().Name.Should().Be("DRIVER_POWER_STATE_FAILURE");
        vm.BlueScreens.Single().Explanation.Should().StartWith("Le plus souvent causé par un pilote");
        vm.AppCrashes.Single().Count.Should().Be(4);
        vm.Devices.Single().Explanation.Should().Contain("ne peut pas démarrer");
        vm.DisksUnavailable.Should().BeTrue();
        vm.RebootPending.Should().BeTrue();
        vm.Repairs.Select(r => r.Kind).Should().Equal(RepairKind.FlushDns, RepairKind.SystemFiles, RepairKind.WindowsImage);
    }

    [Fact]
    public async Task RunRepair_GoesThroughConfirmation_ThenShowsOutput()
    {
        var (vm, runner, fixes) = Make();

        vm.RunRepairCommand.Execute(RepairKind.SystemFiles);
        fixes.IsOpen.Should().BeTrue();
        runner.Last.Should().BeNull("rien ne se lance avant la confirmation");

        await fixes.ConfirmCommand.ExecuteAsync(null);

        runner.Last.Should().Be("sfc.exe");
        vm.RepairOutput.Should().Contain("100 %");
    }
}
