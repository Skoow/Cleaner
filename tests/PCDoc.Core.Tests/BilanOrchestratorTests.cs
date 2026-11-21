using System.Diagnostics;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Bilan;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Core.Tests;

public class BilanOrchestratorTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now { get; } = new(2026, 11, 21, 14, 32, 0, TimeSpan.Zero);
    }

    private sealed class FakeScanner(string id, Func<CancellationToken, Task<IReadOnlyList<Finding>>> scan) : IScanner
    {
        public string ModuleId => id;

        public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken) => scan(cancellationToken);
    }

    private static Finding F(string id, string module, Severity s) =>
        new(id, module, s, new LocalizedText("T"), new LocalizedText("E"), "");

    private static FakeScanner Returning(string id, params Finding[] findings) =>
        new(id, _ => Task.FromResult<IReadOnlyList<Finding>>(findings));

    private static BilanOrchestrator Make(TimeSpan timeout, params IScanner[] scanners) =>
        new(scanners, new FixedClock(), timeout);

    [Fact]
    public async Task Run_MergesFindingsFromAllScanners_SortedBySeverity()
    {
        var sut = Make(TimeSpan.FromSeconds(5),
            Returning("b", F("b.1", "b", Severity.Info), F("b.2", "b", Severity.Critical)),
            Returning("a", F("a.1", "a", Severity.Warning), F("a.2", "a", Severity.Critical)));

        var result = await sut.RunAsync(null, CancellationToken.None);

        result.Findings.Select(f => f.Id).Should().Equal("a.2", "b.2", "a.1", "b.1");
        result.Errors.Should().BeEmpty();
        result.CompletedAt.Should().Be(new FixedClock().Now);
    }

    [Fact]
    public async Task Run_RunsScannersInParallel()
    {
        async Task<IReadOnlyList<Finding>> Slow(CancellationToken ct)
        {
            await Task.Delay(500, ct);
            return [];
        }

        var sut = Make(TimeSpan.FromSeconds(5), new FakeScanner("x", Slow), new FakeScanner("y", Slow));

        var watch = Stopwatch.StartNew();
        await sut.RunAsync(null, CancellationToken.None);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task Run_WhenScannerThrows_ReportsModuleError()
    {
        var sut = Make(TimeSpan.FromSeconds(5),
            new FakeScanner("fake", _ => throw new InvalidOperationException("boom")),
            Returning("ok", F("ok.1", "ok", Severity.Info)));

        var result = await sut.RunAsync(null, CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Module.Should().Be("fake");
        result.Findings.Should().ContainSingle(f => f.Id == "ok.1");
    }

    [Fact]
    public async Task Run_WhenScannerHangs_TimesOut()
    {
        var sut = Make(TimeSpan.FromMilliseconds(100),
            new FakeScanner("stuck", _ => new TaskCompletionSource<IReadOnlyList<Finding>>().Task),
            Returning("ok", F("ok.1", "ok", Severity.Info)));

        var result = await sut.RunAsync(null, CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Module.Should().Be("stuck");
        result.Findings.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_WhenUserCancels_Throws()
    {
        using var cts = new CancellationTokenSource();
        var sut = Make(TimeSpan.FromSeconds(5), new FakeScanner("slow", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        }));

        var run = sut.RunAsync(null, cts.Token);
        await cts.CancelAsync();

        await run.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Run_ReportsProgressPerModule()
    {
        var seen = new List<string>();
        var sut = Make(TimeSpan.FromSeconds(5), Returning("a"), Returning("b"));

        await sut.RunAsync(new SyncProgress(seen.Add), CancellationToken.None);

        seen.Should().BeEquivalentTo("a", "b");
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }
}
