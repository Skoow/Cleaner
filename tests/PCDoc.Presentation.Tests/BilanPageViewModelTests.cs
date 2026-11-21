using PCDoc.Core.Abstractions;
using PCDoc.Core.Bilan;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Core.Modules;
using PCDoc.Modules.System;
using PCDoc.Presentation.Bilan;

namespace PCDoc.Presentation.Tests;

public class BilanPageViewModelTests
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset Now => new(2026, 11, 21, 14, 32, 0, TimeSpan.FromHours(1));
    }

    private sealed class Scanner(string id, Func<CancellationToken, Task<IReadOnlyList<Finding>>> scan) : IScanner
    {
        public string ModuleId => id;

        public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken) => scan(cancellationToken);
    }

    private static Finding F(string id, string module, Severity s, bool fix = false) =>
        new(id, module, s, new LocalizedText("Finding.DiskLow.Title", "C:"),
            new LocalizedText("Finding.DiskLow.Explanation", "C:", 9.8, 476.0, 2.0), "Win32_LogicalDisk C:", FixAvailable: fix);

    private static Scanner Returning(string id, params Finding[] f) => new(id, _ => Task.FromResult<IReadOnlyList<Finding>>(f));

    private static (BilanPageViewModel Vm, Localizer Loc) Make(params IScanner[] scanners)
    {
        var localizer = new Localizer();
        localizer.Register(CoreResources.Manager);
        SystemModule.RegisterResources(localizer);
        var orchestrator = new BilanOrchestrator(scanners, new Clock(), TimeSpan.FromSeconds(5));
        return (new BilanPageViewModel(orchestrator, localizer, new SystemPageViewModelTests.FakeClipboard()), localizer);
    }

    private static readonly Scanner Typical = Returning("system",
        F("a", "system", Severity.Critical), F("b", "system", Severity.Warning), F("c", "system", Severity.Warning),
        F("d", "system", Severity.Info));

    [Fact]
    public async Task Analyze_SetsRunningThenDone()
    {
        var gate = new TaskCompletionSource();
        var (vm, _) = Make(new Scanner("system", async _ => { await gate.Task; return []; }));
        vm.State.Should().Be(BilanState.Idle);

        var run = vm.AnalyzeCommand.ExecuteAsync(null);
        vm.State.Should().Be(BilanState.Running);
        gate.SetResult();
        await run;

        vm.State.Should().Be(BilanState.Done);
    }

    [Fact]
    public async Task Analyze_CountsBySeverity()
    {
        var (vm, _) = Make(Typical);

        await vm.AnalyzeCommand.ExecuteAsync(null);

        (vm.CriticalCount, vm.WarningCount, vm.InfoCount).Should().Be((1, 2, 1));
        vm.Score.Should().Be(70);
        vm.ScoreLabel.Should().Be("À surveiller");
        vm.Findings.Should().HaveCount(4);
        vm.Findings[0].SeverityLabel.Should().Be("Critique");
        vm.Findings[0].IsExpanded.Should().BeTrue();
        vm.CompletedText.Should().StartWith("Analyse terminée à 14:32");
    }

    [Fact]
    public async Task Filter_ByModule()
    {
        var (vm, _) = Make(Typical, Returning("other", F("z", "other", Severity.Info)));
        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.ModuleFilters.Select(m => m.Id).Should().Equal(null, "other", "system");
        vm.ModuleFilter = "other";

        vm.Findings.Select(f => f.Id).Should().Equal("z");
        vm.ModuleFilter = null;
        vm.Findings.Should().HaveCount(5);
    }

    [Fact]
    public async Task Cancel_ReturnsToIdle_WithoutResult()
    {
        var (vm, _) = Make(new Scanner("system", async ct => { await Task.Delay(Timeout.Infinite, ct); return []; }));

        var run = vm.AnalyzeCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null);
        await run;

        vm.State.Should().Be(BilanState.Idle);
        vm.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ModuleError_IsExposed()
    {
        var (vm, _) = Make(new Scanner("system", _ => throw new InvalidOperationException("boom")));

        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.ErrorText.Should().Be("Analyse incomplète : Système");
    }

    [Fact]
    public async Task NoFixAvailable_HidesFixButtons()
    {
        var (vm, _) = Make(Typical);

        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.AnyFixAvailable.Should().BeFalse();
        vm.Findings.Should().OnlyContain(f => !f.ShowFixButton);
    }

    [Fact]
    public async Task FixAvailable_ShowsFixButton()
    {
        var (vm, _) = Make(Returning("system", F("a", "system", Severity.Warning, fix: true)));

        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.AnyFixAvailable.Should().BeTrue();
        vm.Findings[0].ShowFixButton.Should().BeTrue();
    }

    [Fact]
    public async Task LanguageSwitch_UpdatesTitlesWithoutRescan()
    {
        var calls = 0;
        var (vm, loc) = Make(new Scanner("system", _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<Finding>>([F("a", "system", Severity.Warning)]);
        }));
        await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.Findings[0].Title.Should().Be("Disque C: presque plein");

        loc.Language = AppLanguage.English;

        vm.Findings[0].Title.Should().Be("Drive C: is almost full");
        vm.Findings[0].SeverityLabel.Should().Be("Warning");
        vm.ScoreLabel.Should().Be("Good condition");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task CopyReport_ContainsTitlesAndDetails()
    {
        var localizer = new Localizer();
        localizer.Register(CoreResources.Manager);
        SystemModule.RegisterResources(localizer);
        var clipboard = new SystemPageViewModelTests.FakeClipboard();
        var vm = new BilanPageViewModel(new BilanOrchestrator([Typical], new Clock(), TimeSpan.FromSeconds(5)), localizer, clipboard);
        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.CopyReportCommand.Execute(null);

        clipboard.Text.Should().Contain("PCDoc").And.Contain("70/100").And.Contain("Disque C: presque plein").And.Contain("Win32_LogicalDisk C:");
    }
}
