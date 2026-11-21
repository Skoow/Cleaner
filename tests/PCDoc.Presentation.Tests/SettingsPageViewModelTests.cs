using System.Xml.Linq;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Presentation.Settings;

namespace PCDoc.Presentation.Tests;

public class SettingsPageViewModelTests
{
    private sealed class MemoryStore : ISettingsStore
    {
        public AppSettings Current = new(AppLanguage.French, ThemeChoice.System);

        public AppSettings Load() => Current;

        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class Theme : IThemeApplier
    {
        public ThemeChoice? Applied;

        public void Apply(ThemeChoice theme) => Applied = theme;
    }

    private sealed class Profiles(bool isProcessUser) : IUserProfileResolver
    {
        public UserProfile Resolve() => new("S-1-5-21-1", @"PC\Lea", @"C:\Users\Lea", "r", "l", isProcessUser);
    }

    private sealed class Cleaner : IAppDataCleaner
    {
        public int Calls;

        public void Clear() => Calls++;
    }

    private static (SettingsPageViewModel Vm, MemoryStore Store, Theme Theme, Localizer Loc, Cleaner Cleaner) Make(bool isProcessUser = false)
    {
        var loc = new Localizer();
        loc.Register(CoreResources.Manager);
        var store = new MemoryStore();
        var theme = new Theme();
        var cleaner = new Cleaner();
        var vm = new SettingsPageViewModel(loc, store, theme, new Profiles(isProcessUser), cleaner,
            new SystemPageViewModelTests.FakeClipboard(), @"C:\Users\Admin\AppData\Local\PCDoc\logs", () => "GNU GENERAL PUBLIC LICENSE");
        return (vm, store, theme, loc, cleaner);
    }

    [Fact]
    public void ChangeLanguage_SavesAndAppliesImmediately()
    {
        var (vm, store, _, loc, _) = Make();

        vm.Language = AppLanguage.English;

        loc.Language.Should().Be(AppLanguage.English);
        store.Current.Language.Should().Be(AppLanguage.English);
    }

    [Fact]
    public void ChangeTheme_CallsThemeApplier()
    {
        var (vm, store, theme, _, _) = Make();

        vm.Theme = ThemeChoice.Dark;

        theme.Applied.Should().Be(ThemeChoice.Dark);
        store.Current.Theme.Should().Be(ThemeChoice.Dark);
    }

    [Fact]
    public void AnalyzedAccount_ShowsTargetProfile_AndMentionWhenDifferent()
    {
        Make(isProcessUser: false).Vm.AnalyzedAccount.Should().Contain(@"PC\Lea").And.Contain("autre compte");
        Make(isProcessUser: true).Vm.AnalyzedAccount.Should().Be(@"PC\Lea");
    }

    [Fact]
    public void ClearPcdocData_AsksConfirmationFirst()
    {
        var (vm, _, _, _, cleaner) = Make();

        vm.RequestClearCommand.Execute(null);

        vm.ConfirmingClear.Should().BeTrue();
        cleaner.Calls.Should().Be(0);
    }

    [Fact]
    public void ClearPcdocData_DeletesLogsAndSettings_ThenDefaults()
    {
        var (vm, store, theme, _, cleaner) = Make();
        vm.Theme = ThemeChoice.Dark;
        vm.RequestClearCommand.Execute(null);

        vm.ConfirmClearCommand.Execute(null);

        cleaner.Calls.Should().Be(1);
        vm.ConfirmingClear.Should().BeFalse();
        vm.Theme.Should().Be(ThemeChoice.System);
        theme.Applied.Should().Be(ThemeChoice.System);
        vm.ClearDone.Should().BeTrue();
        store.Current.Theme.Should().Be(ThemeChoice.Dark, "les paramètres effacés ne sont pas réécrits");
    }

    [Fact]
    public void LogsPath_IsShownAndCopied()
    {
        var clipboard = new SystemPageViewModelTests.FakeClipboard();
        var loc = new Localizer();
        var vm = new SettingsPageViewModel(loc, new MemoryStore(), new Theme(), new Profiles(true), new Cleaner(), clipboard,
            @"C:\x\PCDoc\logs", () => null);

        vm.CopyLogsPathCommand.Execute(null);

        vm.LogsPath.Should().Be(@"C:\x\PCDoc\logs");
        clipboard.Text.Should().Be(@"C:\x\PCDoc\logs");
    }

    [Fact]
    public void License_LoadedOnDemand()
    {
        var (vm, _, _, _, _) = Make();
        vm.LicenseText.Should().BeNull();

        vm.ShowLicenseCommand.Execute(null);

        vm.LicenseText.Should().StartWith("GNU GENERAL PUBLIC LICENSE");
    }

    [Fact]
    public void Components_MatchCentralPackageVersions()
    {
        var props = FindUp("Directory.Packages.props");
        var versions = XDocument.Load(props).Descendants("PackageVersion")
            .ToDictionary(e => (string)e.Attribute("Include")!, e => (string)e.Attribute("Version")!);

        foreach (var c in ThirdPartyComponents.All.Where(c => c.PackageId is not null))
        {
            versions.Should().ContainKey(c.PackageId!);
            c.Version.Should().Be(versions[c.PackageId!], $"{c.Name} doit afficher la version réellement utilisée");
        }

        ThirdPartyComponents.All.Should().Contain(c => c.Name == ".NET" && c.License == "MIT");
        ThirdPartyComponents.All.Should().OnlyContain(c => !string.IsNullOrEmpty(c.License) && c.Url.StartsWith("https://", StringComparison.Ordinal));
    }

    private static string FindUp(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, file)))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new FileNotFoundException(file), file);
    }
}
