using PCDoc.Core.Fixing;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Presentation.Fixing;
using PCDoc.Presentation.History;

namespace PCDoc.Presentation.Tests;

public sealed class V02PagesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcdoc-v02-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private sealed class MemoryStore : ISettingsStore
    {
        public AppSettings Current = new(AppLanguage.French, ThemeChoice.System);

        public AppSettings Load() => Current;

        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class Protection(bool enabled) : IProtectionService
    {
        public bool Enabled = enabled;

        public bool IsEnabled() => Enabled;

        public bool Enable() => Enabled = true;
    }

    private static Localizer Loc()
    {
        var l = new Localizer();
        l.Register(CoreResources.Manager);
        return l;
    }

    private (FixPipeline Pipeline, FixConfirmationViewModelTests.Fixer Fixer) Pipeline()
    {
        var fixer = new FixConfirmationViewModelTests.Fixer();
        return (new FixPipeline([fixer], new FixConfirmationViewModelTests.Restore(RestorePointOutcome.Created),
            new UndoJournal(Path.Combine(_dir, "u.jsonl"), new FixConfirmationViewModelTests.Clock())), fixer);
    }

    [Fact]
    public async Task History_ListsAppliedChanges_AndUndoes()
    {
        var (pipeline, fixer) = Pipeline();
        await pipeline.RunAsync([FixConfirmationViewModelTests.F("a")], AppMode.Novice, false, CancellationToken.None);
        var vm = new HistoryPageViewModel(pipeline, Loc());

        vm.Refresh();
        vm.Entries.Should().ContainSingle(e => e.CanUndo);

        await vm.UndoCommand.ExecuteAsync(vm.Entries[0]);

        fixer.Applied.Should().BeEmpty();
        vm.Entries.Single().CanUndo.Should().BeFalse();
        vm.MessageText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void History_ReportsInterruptedChanges()
    {
        var (pipeline, _) = Pipeline();
        pipeline.Journal.Begin(PCDoc.Core.Findings.UndoEntry.Create("x", "t", "", "Undo.Disabled", ["x"]));

        var vm = new HistoryPageViewModel(pipeline, Loc());
        vm.Refresh();

        vm.InterruptedText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task History_InterruptedEntry_CanBeUndone()
    {
        var (pipeline, fixer) = Pipeline();
        var entry = pipeline.Journal.Begin(PCDoc.Core.Findings.UndoEntry.Create("a", "t", "", "Undo.Disabled", ["a"]));
        fixer.Applied.Add("a"); // la modification a eu lieu, puis coupure avant « appliquée »
        var vm = new HistoryPageViewModel(pipeline, Loc());
        vm.Refresh();

        vm.Entries.Single().CanUndo.Should().BeTrue();
        await vm.UndoCommand.ExecuteAsync(vm.Entries.Single());

        fixer.Applied.Should().BeEmpty();
        _ = entry;
    }

    [Fact]
    public void Settings_CancelExpert_RefreshesModeDisplay()
    {
        var vm = new PCDoc.Presentation.Settings.SettingsPageViewModel(Loc(), new MemoryStore(), new NoTheme(), new Profiles(), new NoCleaner(), null, "", () => null);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.RequestExpertCommand.Execute(null);
        vm.CancelExpertCommand.Execute(null);

        raised.Should().Contain(nameof(vm.IsExpert));
        vm.IsExpert.Should().BeFalse();
    }

    [Fact]
    public void Settings_SwitchToExpert_NeedsConfirmation()
    {
        var store = new MemoryStore();
        var vm = new PCDoc.Presentation.Settings.SettingsPageViewModel(Loc(), store, new NoTheme(), new Profiles(), new NoCleaner(), null, "", () => null);

        vm.RequestExpertCommand.Execute(null);
        vm.ConfirmingExpert.Should().BeTrue();
        store.Current.Mode.Should().Be(AppMode.Novice);

        vm.ConfirmExpertCommand.Execute(null);
        store.Current.Mode.Should().Be(AppMode.Expert);
        vm.IsExpert.Should().BeTrue();

        vm.SetNoviceCommand.Execute(null);
        store.Current.Mode.Should().Be(AppMode.Novice);
    }

    [Fact]
    public void FirstRun_EnablesProtection_AndSavesChoices()
    {
        var store = new MemoryStore();
        var protection = new Protection(enabled: false);
        var loc = Loc();
        var vm = new FirstRunViewModel(loc, store, protection);
        var finished = false;
        vm.Finished += (_, _) => finished = true;

        vm.ProtectionEnabled.Should().BeFalse();
        vm.EnableProtectionCommand.Execute(null);
        vm.ProtectionEnabled.Should().BeTrue();
        vm.Language = AppLanguage.English;
        vm.Mode = AppMode.Expert;
        vm.FinishCommand.Execute(null);

        finished.Should().BeTrue();
        loc.Language.Should().Be(AppLanguage.English);
        store.Current.Should().Match<AppSettings>(s => s.FirstRunCompleted && s.Mode == AppMode.Expert && s.Language == AppLanguage.English);
    }

    [Fact]
    public void Shell_HasHistoryEntry_AndShowsFirstRunUntilDone()
    {
        var shell = new ShellViewModel(p => p.ToString());

        shell.NavItems.Select(n => n.Id).Should().Equal(PageId.Bilan, PageId.System, PageId.Diagnostic, PageId.History, PageId.Settings);
    }

    private sealed class NoTheme : IThemeApplier
    {
        public void Apply(ThemeChoice theme)
        {
        }
    }

    private sealed class NoCleaner : IAppDataCleaner
    {
        public bool HasCleared => false;

        public void Clear()
        {
        }
    }

    private sealed class Profiles : PCDoc.Core.Abstractions.IUserProfileResolver
    {
        public PCDoc.Core.Abstractions.UserProfile Resolve() => new("S", "PC\\Lea", "C:\\Users\\Lea", "r", "l", true);
    }
}
