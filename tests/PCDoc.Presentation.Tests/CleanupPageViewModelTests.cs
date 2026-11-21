using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Fixing;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Modules.Cleanup;
using PCDoc.Presentation.Cleanup;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Tests;

public sealed class CleanupPageViewModelTests : IDisposable
{
    private const long MB = 1024 * 1024;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcdoc-cl-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private sealed class Source : ICleanupSource
    {
        public CleanupReport Analyze() => new(
            [new CategoryResult("windows-temp", RiskLevel.Safe, 300 * MB, 12), new CategoryResult("crash-dumps", RiskLevel.Conditional, 800 * MB, 1),
             new CategoryResult("chrome-cache", RiskLevel.Safe, 0, 0)],
            2048 * MB, 30);

        public IReadOnlyList<FileEntry> BigFiles() => [new(@"C:\Users\Lea\Downloads\movie.mkv", 4000 * MB, DateTimeOffset.Now)];
    }

    private sealed class RecordingFixer : PCDoc.Core.Modules.IFixer
    {
        public List<string> Applied { get; } = [];

        public string UndoKind => "cleanup";

        public bool SupportsUndo => false;

        public bool CanFix(Finding finding) => true;

        public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken ct) => Task.FromResult(UndoEntry.Create(finding.Id, "cleanup", "", "T", []));

        public Task ApplyAsync(Finding finding, CancellationToken ct)
        {
            Applied.Add(finding.Id);
            return Task.CompletedTask;
        }

        public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken ct) => Task.FromResult(false);

        public Task UndoAsync(UndoEntry entry, CancellationToken ct) => throw new NotSupportedException();
    }

    private (CleanupPageViewModel Vm, RecordingFixer Fixer, FixConfirmationViewModel Fixes) Make(AppMode mode = AppMode.Novice)
    {
        var loc = new Localizer();
        loc.Register(CoreResources.Manager);
        CleanupModule.RegisterResources(loc);
        var fixer = new RecordingFixer();
        var pipeline = new FixPipeline([fixer], new FixConfirmationViewModelTests.Restore(RestorePointOutcome.Created),
            new UndoJournal(Path.Combine(_dir, "u.jsonl"), new FixConfirmationViewModelTests.Clock()));
        var fixes = new FixConfirmationViewModel(pipeline, loc, () => mode);
        return (new CleanupPageViewModel(new Source(), loc, fixes, new SystemPageViewModelTests.FakeClipboard()), fixer, fixes);
    }

    [Fact]
    public async Task Analyze_BuildsRows_SelectsNonEmptySafeOnly()
    {
        var (vm, _, _) = Make();

        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.Categories.Select(c => c.Id).Should().Equal("windows-temp", "crash-dumps", "chrome-cache", CleanupEngine.RecycleBinId, CleanupEngine.ComponentStoreId);
        vm.Categories.Where(c => c.IsSelected).Select(c => c.Id).Should().Equal("windows-temp", CleanupEngine.ComponentStoreId);
        vm.Categories[0].Size.Should().Be("300 Mo");
        vm.Categories[0].Title.Should().Be("Fichiers temporaires de Windows");
        vm.TotalText.Should().Contain("300 Mo");
        vm.BigFiles.Single().Size.Should().Be("3,9 Go");
    }

    [Fact]
    public async Task CleanSelected_GoesThroughConfirmation()
    {
        var (vm, fixer, fixes) = Make();
        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.CleanSelectedCommand.Execute(null);
        fixes.Items.Select(i => i.Id).Should().Equal("cleanup.windows-temp", "cleanup.component-store");
        await fixes.ConfirmCommand.ExecuteAsync(null);

        fixer.Applied.Should().Equal("cleanup.windows-temp", "cleanup.component-store");
    }

    [Fact]
    public async Task Novice_CannotCleanConditional_EvenIfChecked()
    {
        var (vm, _, fixes) = Make(AppMode.Novice);
        await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.Categories.Single(c => c.Id == CleanupEngine.RecycleBinId).CanSelect.Should().BeFalse();

        vm.Categories.Single(c => c.Id == CleanupEngine.RecycleBinId).IsSelected = true;
        vm.CleanSelectedCommand.Execute(null);

        fixes.Items.Select(i => i.Id).Should().NotContain("cleanup.recycle-bin");
    }
}
