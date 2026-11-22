using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Fixing;
using PCDoc.Core.Modules;
using PCDoc.Core.Settings;

namespace PCDoc.Core.Tests;

public sealed class FixPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcdoc-fix-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRestore(RestorePointOutcome outcome) : IRestorePointService
    {
        public int Calls;

        public Task<RestorePointOutcome> EnsureAsync(string description, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(outcome);
        }
    }

    /// <summary>Faux système : une valeur par élément ; la correction la met à « off ».</summary>
    private sealed class FakeFixer(Dictionary<string, string> state, UndoJournal? journal = null) : IFixer
    {
        public List<string> Log { get; } = [];

        public bool Throw { get; set; }

        public string UndoKind => "fake";

        public bool CanFix(Finding finding) => finding.Id.StartsWith("fake.", StringComparison.Ordinal);

        public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken ct) =>
            Task.FromResult(UndoEntry.Create(finding.Id, UndoKind, state[finding.Id], "Undo.Fake", [finding.Id]));

        public Task ApplyAsync(Finding finding, CancellationToken ct)
        {
            // Au moment où le système est modifié, l'entrée « en attente » doit déjà être sur le disque.
            Log.Add(journal?.Entries().Any(e => e.FindingId == finding.Id && e.Status == UndoStatus.Pending) == true ? "pending-before-apply" : "apply");
            if (Throw)
            {
                throw new InvalidOperationException("access denied");
            }

            state[finding.Id] = "off";
            return Task.CompletedTask;
        }

        public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken ct) =>
            Task.FromResult(state[entry.FindingId] == "off");

        public Task UndoAsync(UndoEntry entry, CancellationToken ct)
        {
            state[entry.FindingId] = entry.Payload;
            return Task.CompletedTask;
        }
    }

    private static Finding F(string id, RiskLevel risk = RiskLevel.Safe) =>
        new(id, "system", Severity.Info, new LocalizedText("T"), new LocalizedText("E"), "", true, risk);

    private (FixPipeline Pipeline, FakeFixer Fixer, FakeRestore Restore, UndoJournal Journal, Dictionary<string, string> State) Make(
        RestorePointOutcome restore = RestorePointOutcome.Created)
    {
        var state = new Dictionary<string, string> { ["fake.a"] = "on", ["fake.b"] = "on" };
        var journal = new UndoJournal(Path.Combine(_dir, "undo.jsonl"), new Clock());
        var fixer = new FakeFixer(state, journal);
        var r = new FakeRestore(restore);
        return (new FixPipeline([fixer], r, journal), fixer, r, journal, state);
    }

    [Fact]
    public async Task Run_CreatesOneRestorePoint_ThenAppliesAll()
    {
        var (sut, _, restore, journal, state) = Make();

        var result = await sut.RunAsync([F("fake.a"), F("fake.b")], AppMode.Novice, allowWithoutRestorePoint: false, CancellationToken.None);

        restore.Calls.Should().Be(1);
        result.Outcomes.Should().OnlyContain(o => o.Status == FixStatus.Applied);
        state.Values.Should().OnlyContain(v => v == "off");
        journal.Entries().Should().HaveCount(2).And.OnlyContain(e => e.Status == UndoStatus.Applied);
    }

    private sealed class MemoryLog : PCDoc.Core.Activity.IActivityLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string category, string message) => Lines.Add($"[{category}] {message}");

        public IReadOnlyList<string> ReadAll() => Lines;
    }

    private sealed class ThrowingCanFix : PCDoc.Core.Modules.IFixer
    {
        public string UndoKind => "throws";

        public bool CanFix(Finding finding) => throw new ArgumentNullException(nameof(finding));

        public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken ct) => throw new NotSupportedException();

        public Task ApplyAsync(Finding finding, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken ct) => throw new NotSupportedException();

        public Task UndoAsync(UndoEntry entry, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Run_WritesEachActionToTheActivityLog_AndSurvivesAFixerThatThrowsInCanFix()
    {
        var state = new Dictionary<string, string> { ["fake.a"] = "on" };
        var journal = new UndoJournal(Path.Combine(_dir, "undo2.jsonl"), new Clock());
        var log = new MemoryLog();
        var sut = new FixPipeline([new ThrowingCanFix(), new FakeFixer(state, journal)], new FakeRestore(RestorePointOutcome.Created), journal)
        {
            Activity = log,
            Describe = t => t.Key,
        };

        var result = await sut.RunAsync([F("fake.a")], AppMode.Novice, false, CancellationToken.None);

        result.Outcomes.Should().ContainSingle().Which.Status.Should().Be(FixStatus.Applied);
        log.Lines.Should().Equal("[restore] Activity.Restore.Created", "[system] Activity.Applied");
    }

    [Fact]
    public async Task Run_WritesPendingEntryBeforeChangingTheSystem()
    {
        var (sut, fixer, _, _, _) = Make();

        await sut.RunAsync([F("fake.a")], AppMode.Novice, false, CancellationToken.None);

        fixer.Log.Should().Equal("pending-before-apply");
    }

    [Theory]
    [InlineData(RestorePointOutcome.ProtectionDisabled)]
    [InlineData(RestorePointOutcome.Failed)]
    public async Task Run_WithoutRestorePoint_StopsUntilConfirmed(RestorePointOutcome outcome)
    {
        var (sut, _, _, journal, state) = Make(outcome);

        var first = await sut.RunAsync([F("fake.a")], AppMode.Novice, allowWithoutRestorePoint: false, CancellationToken.None);

        first.NeedsConfirmationWithoutRestorePoint.Should().BeTrue();
        state["fake.a"].Should().Be("on");
        journal.Entries().Should().BeEmpty();

        var second = await sut.RunAsync([F("fake.a")], AppMode.Novice, allowWithoutRestorePoint: true, CancellationToken.None);
        second.Outcomes.Single().Status.Should().Be(FixStatus.Applied);
    }

    [Fact]
    public async Task Run_ReusedRecentRestorePoint_Proceeds()
    {
        var (sut, _, _, _, _) = Make(RestorePointOutcome.ReusedRecent);

        var result = await sut.RunAsync([F("fake.a")], AppMode.Novice, false, CancellationToken.None);

        result.RestorePoint.Should().Be(RestorePointOutcome.ReusedRecent);
        result.Outcomes.Single().Status.Should().Be(FixStatus.Applied);
    }

    [Theory]
    [InlineData(RiskLevel.Discouraged, AppMode.Novice)]
    [InlineData(RiskLevel.Locked, AppMode.Expert)]
    public async Task Run_NotAllowedRisk_IsRefused(RiskLevel risk, AppMode mode)
    {
        var (sut, _, restore, _, state) = Make();

        var result = await sut.RunAsync([F("fake.a", risk)], mode, false, CancellationToken.None);

        result.Outcomes.Single().Status.Should().Be(FixStatus.NotAllowed);
        state["fake.a"].Should().Be("on");
        restore.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Run_FixerThrows_MarksFailed_OthersContinue()
    {
        var state = new Dictionary<string, string> { ["fake.a"] = "on" };
        var journal = new UndoJournal(Path.Combine(_dir, "undo.jsonl"), new Clock());
        var throwing = new FakeFixer(state) { Throw = true };
        var sut = new FixPipeline([throwing], new FakeRestore(RestorePointOutcome.Created), journal);

        var result = await sut.RunAsync([F("fake.a"), F("other.x")], AppMode.Novice, false, CancellationToken.None);

        result.Outcomes.Select(o => o.Status).Should().Equal(FixStatus.Failed, FixStatus.NoFixer);
        journal.Entries().Single().Status.Should().Be(UndoStatus.Failed);
    }

    [Fact]
    public async Task Undo_RestoresAndMarksUndone()
    {
        var (sut, _, _, journal, state) = Make();
        await sut.RunAsync([F("fake.a")], AppMode.Novice, false, CancellationToken.None);

        var outcome = await sut.UndoAsync(journal.Entries().Single(), CancellationToken.None);

        outcome.Should().Be(UndoOutcome.Undone);
        state["fake.a"].Should().Be("on");
        journal.Entries().Single().Status.Should().Be(UndoStatus.Undone);
    }

    [Fact]
    public async Task Undo_WhenStateChangedSince_Refuses()
    {
        var (sut, _, _, journal, state) = Make();
        await sut.RunAsync([F("fake.a")], AppMode.Novice, false, CancellationToken.None);
        state["fake.a"] = "changed-by-windows-update";

        (await sut.UndoAsync(journal.Entries().Single(), CancellationToken.None)).Should().Be(UndoOutcome.StateChanged);
        state["fake.a"].Should().Be("changed-by-windows-update");
    }

    [Fact]
    public void Journal_InterruptedEntry_IsReportedAsPending()
    {
        var path = Path.Combine(_dir, "undo.jsonl");
        var journal = new UndoJournal(path, new Clock());
        journal.Begin(UndoEntry.Create("fake.a", "fake", "on", "Undo.Fake", ["fake.a"]));

        // Nouveau démarrage : on relit le fichier.
        new UndoJournal(path, new Clock()).Interrupted().Should().ContainSingle(e => e.FindingId == "fake.a");
    }

    private sealed class NoUndoFixer : IFixer
    {
        public string UndoKind => "repair";

        public bool SupportsUndo => false;

        public bool CanFix(Finding finding) => false;

        public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken ct) => throw new NotSupportedException();

        public Task ApplyAsync(Finding finding, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken ct) => Task.FromResult(false);

        public Task UndoAsync(UndoEntry entry, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public void ResolveInterrupted_MarksNonUndoableAsFailed_KeepsUndoable()
    {
        var journal = new UndoJournal(Path.Combine(_dir, "undo.jsonl"), new Clock());
        var (_, fixer, _, _, _) = Make();
        var sut = new FixPipeline([fixer, new NoUndoFixer()], new FakeRestore(RestorePointOutcome.Created), journal);
        journal.Begin(UndoEntry.Create("repair.sfc", "repair", "", "T", []));
        journal.Begin(UndoEntry.Create("fake.a", "fake", "on", "T", []));

        sut.ResolveInterrupted();

        journal.Interrupted().Select(e => e.FindingId).Should().Equal("fake.a");
        journal.Entries().Single(e => e.FindingId == "repair.sfc").Status.Should().Be(UndoStatus.Failed);
    }

    [Fact]
    public void Journal_CorruptLine_IsSkipped()
    {
        var path = Path.Combine(_dir, "undo.jsonl");
        var journal = new UndoJournal(path, new Clock());
        var entry = journal.Begin(UndoEntry.Create("fake.a", "fake", "on", "Undo.Fake", []));
        journal.Mark(entry.Id, UndoStatus.Applied);
        File.AppendAllText(path, "{ broken\n");

        new UndoJournal(path, new Clock()).Entries().Single().Status.Should().Be(UndoStatus.Applied);
    }
}
