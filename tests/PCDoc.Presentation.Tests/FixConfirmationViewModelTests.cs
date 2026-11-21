using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Fixing;
using PCDoc.Core.Localization;
using PCDoc.Core.Modules;
using PCDoc.Core.Settings;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Tests;

public sealed class FixConfirmationViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcdoc-fixvm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    internal sealed class Clock : IClock
    {
        public DateTimeOffset Now => new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
    }

    internal sealed class Restore(RestorePointOutcome outcome) : IRestorePointService
    {
        public Task<RestorePointOutcome> EnsureAsync(string description, CancellationToken ct) => Task.FromResult(outcome);
    }

    internal sealed class Fixer : IFixer
    {
        public List<string> Applied { get; } = [];

        public string UndoKind => "t";

        public bool CanFix(Finding finding) => true;

        public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken ct) =>
            Task.FromResult(UndoEntry.Create(finding.Id, "t", "", "Undo.Disabled", [finding.Id]));

        public Task ApplyAsync(Finding finding, CancellationToken ct)
        {
            Applied.Add(finding.Id);
            return Task.CompletedTask;
        }

        public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken ct) => Task.FromResult(true);

        public Task UndoAsync(UndoEntry entry, CancellationToken ct)
        {
            Applied.Remove(entry.FindingId);
            return Task.CompletedTask;
        }
    }

    internal static Finding F(string id, RiskLevel risk = RiskLevel.Safe, bool reversible = true) =>
        new(id, "system", Severity.Info, new LocalizedText("Nav.System"), new LocalizedText("Nav.Bilan"), "", true, risk, reversible);

    private (FixConfirmationViewModel Vm, Fixer Fixer) Make(RestorePointOutcome restore = RestorePointOutcome.Created, AppMode mode = AppMode.Novice)
    {
        var fixer = new Fixer();
        var pipeline = new FixPipeline([fixer], new Restore(restore), new UndoJournal(Path.Combine(_dir, "u.jsonl"), new Clock()));
        var loc = new Localizer();
        loc.Register(CoreResources.Manager);
        return (new FixConfirmationViewModel(pipeline, loc, () => mode), fixer);
    }

    [Fact]
    public void Request_ShowsListAndRisk_NothingApplied()
    {
        var (vm, fixer) = Make();

        vm.Request([F("a"), F("b")]);

        vm.IsOpen.Should().BeTrue();
        vm.Items.Should().HaveCount(2);
        vm.NeedsStrongConfirmation.Should().BeFalse();
        fixer.Applied.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_AppliesAndReportsCount()
    {
        var (vm, fixer) = Make();
        var completed = 0;
        vm.Completed += (_, _) => completed++;
        vm.Request([F("a"), F("b")]);

        await vm.ConfirmCommand.ExecuteAsync(null);

        fixer.Applied.Should().Equal("a", "b");
        vm.IsOpen.Should().BeFalse();
        vm.ResultText.Should().Contain("2");
        completed.Should().Be(1);
    }

    [Fact]
    public void Discouraged_NeedsStrongConfirmation()
    {
        var (vm, _) = Make(mode: AppMode.Expert);

        vm.Request([F("a", RiskLevel.Discouraged)]);

        vm.NeedsStrongConfirmation.Should().BeTrue();
        vm.ConfirmCommand.CanExecute(null).Should().BeFalse("la case « Je comprends le risque » doit être cochée");
        vm.RiskAcknowledged = true;
        vm.ConfirmCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task NoRestorePoint_AsksSecondConfirmation_ThenApplies()
    {
        var (vm, fixer) = Make(RestorePointOutcome.ProtectionDisabled);
        vm.Request([F("a")]);

        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.AwaitingNoRestorePointConfirmation.Should().BeTrue();
        fixer.Applied.Should().BeEmpty();

        await vm.ConfirmWithoutRestorePointCommand.ExecuteAsync(null);

        fixer.Applied.Should().Equal("a");
        vm.AwaitingNoRestorePointConfirmation.Should().BeFalse();
    }

    [Fact]
    public void Cancel_ClosesWithoutApplying()
    {
        var (vm, fixer) = Make();
        vm.Request([F("a")]);

        vm.CancelCommand.Execute(null);

        vm.IsOpen.Should().BeFalse();
        fixer.Applied.Should().BeEmpty();
    }

    [Fact]
    public void Request_NotAllowedInNovice_IsFilteredOut()
    {
        var (vm, _) = Make(mode: AppMode.Novice);

        vm.Request([F("a"), F("b", RiskLevel.Discouraged), F("c", RiskLevel.Locked)]);

        vm.Items.Select(i => i.Id).Should().Equal("a");
    }
}
