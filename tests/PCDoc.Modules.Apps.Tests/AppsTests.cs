using System.Text;
using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.Apps.Tests;

public class AppsTests
{
    private const string Key = Win32AppReader.UninstallPath;
    private const string MsiCode = "{11111111-2222-3333-4444-555555555555}";

    private static FakeRegistryReader Registry()
    {
        var r = new FakeRegistryReader();
        void Add(string key, RegistryView view, params (string Name, object Value)[] values)
        {
            foreach (var (name, value) in values)
            {
                r.Set(RegistryHive.LocalMachine, $@"{Key}\{key}", name, value, view);
            }
        }

        Add(MsiCode, RegistryView.Registry64, ("DisplayName", "Contoso Viewer"), ("WindowsInstaller", 1), ("EstimatedSize", 2048),
            ("InstallDate", "20250102"), ("UninstallString", $"MsiExec.exe /X{MsiCode}"), ("Publisher", "Contoso"));
        Add("Notes_is1", RegistryView.Registry32, ("DisplayName", "Notes"), ("UninstallString", "\"C:\\Program Files (x86)\\Notes\\unins000.exe\""),
            ("QuietUninstallString", "\"C:\\Program Files (x86)\\Notes\\unins000.exe\" /SILENT"),
            ("InstallLocation", @"C:\Program Files (x86)\Notes\"));
        Add("Player", RegistryView.Registry64, ("DisplayName", "Player"), ("UninstallString", @"C:\Program Files\Player\uninstall.exe /x"));
        Add("Quiet", RegistryView.Registry64, ("DisplayName", "Quiet"), ("UninstallString", @"C:\Program Files\Quiet\u.exe"),
            ("QuietUninstallString", "\"C:\\Program Files\\Quiet\\u.exe\" /S"));
        Add("Evil", RegistryView.Registry64, ("DisplayName", "Evil"), ("UninstallString", @"C:\Users\Public\evil.exe"));
        Add("Hidden", RegistryView.Registry64, ("DisplayName", "Hidden"), ("SystemComponent", 1));
        Add("KB1", RegistryView.Registry64, ("DisplayName", "Update"), ("ParentKeyName", "Office"));
        Add("Patch", RegistryView.Registry64, ("DisplayName", "Patch"), ("ReleaseType", "Security Update"));
        Add("NoName", RegistryView.Registry64, ("UninstallString", @"C:\Program Files\x.exe"));
        Add("Dll", RegistryView.Registry64, ("DisplayName", "Dll tool"), ("UninstallString", @"C:\Program Files\Dll\u.exe C:\ProgramData\Vendor\x.dll,Uninstall"));
        Add("Edge", RegistryView.Registry64, ("DisplayName", "Microsoft Edge WebView2 Runtime"), ("UninstallString", @"C:\Program Files\E\setup.exe"));
        Add("Vc", RegistryView.Registry64, ("DisplayName", "Microsoft Visual C++ 2015-2022 Redistributable"), ("UninstallString", @"C:\Program Files\vc.exe"));
        r.Set(RegistryHive.TargetUser, $@"{Key}\Chat", "DisplayName", "Chat");
        r.Set(RegistryHive.TargetUser, $@"{Key}\Chat", "UninstallString", @"C:\Users\Lea\AppData\Local\Chat\Update.exe --uninstall");
        return r;
    }

    private static string StoreOutput(object payload) =>
        "PCDOC:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    private static readonly object StorePayload = new
    {
        Packages = new object[]
        {
            new { N = "Microsoft.BingWeather", F = "Microsoft.BingWeather_4.53.0.0_x64__8wekyb3d8bbwe", Fa = "Microsoft.BingWeather_8wekyb3d8bbwe", P = "CN=Microsoft Corporation, O=Microsoft Corporation, C=US", V = "4.53.0.0", L = @"C:\Program Files\WindowsApps\x", Fw = false, Nr = false, S = "Store" },
            new { N = "Microsoft.VCLibs.140.00", F = "Microsoft.VCLibs.140.00_14.0_x64__8wekyb3d8bbwe", Fa = "Microsoft.VCLibs.140.00_8wekyb3d8bbwe", P = "CN=Microsoft", V = "14.0", L = (string?)null, Fw = true, Nr = false, S = "Store" },
            new { N = "Microsoft.Windows.ShellExperienceHost", F = "Microsoft.Windows.ShellExperienceHost_10.0_neutral_neutral_cw5n1h2txyewy", Fa = "Microsoft.Windows.ShellExperienceHost_cw5n1h2txyewy", P = "CN=Microsoft", V = "10.0", L = (string?)null, Fw = false, Nr = true, S = "System" },
            new { N = "Evil", F = "Evil'; Remove-Item C:\\ -Recurse; '", Fa = "Evil_x", P = "x", V = "1", L = (string?)null, Fw = false, Nr = false, S = "Store" },
            new { N = "Microsoft.LanguageExperiencePackfr-FR", F = "Microsoft.LanguageExperiencePackfr-FR_1.0_neutral__8wekyb3d8bbwe", Fa = "Microsoft.LanguageExperiencePackfr-FR_8wekyb3d8bbwe", P = "CN=Microsoft Corporation, O=Microsoft Corporation, C=US", V = "1.0", L = (string?)null, Fw = false, Nr = false, S = "Store" },
            new { N = "Microsoft.Unknown", F = "Microsoft.Unknown_1.0_x64__8wekyb3d8bbwe", Fa = "Microsoft.Unknown_8wekyb3d8bbwe", P = "CN=Microsoft Corporation, O=Microsoft Corporation, C=US", V = "1.0", L = (string?)null, Fw = false, Nr = false, S = "Store" },
            new { N = "SpotifyAB.SpotifyMusic", F = "SpotifyAB.SpotifyMusic_1.0_x86__zpdnekdrzrea0", Fa = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", P = "CN=Spotify", V = "1.0", L = (string?)null, Fw = false, Nr = false, S = "Developer" },
        },
        Provisioned = new object[]
        {
            new { D = "Microsoft.BingWeather", N = "Microsoft.BingWeather_4.53.0.0_neutral_~_8wekyb3d8bbwe" },
            new { D = "Microsoft.BingNews", N = "Microsoft.BingNews_4.1.0.0_neutral_~_8wekyb3d8bbwe" },
            new { D = "Microsoft.Other", N = "Microsoft.Other_1.0_neutral_~_8wekyb3d8bbwe" },
        },
    };

    private static (AppsEngine Engine, FakeRunner Runner, FakeLauncher Launcher, FakeQuarantine Quarantine, FakeRegistryReader Registry) Create(
        bool isProcessUser = true)
    {
        var registry = Registry();
        var runner = new FakeRunner();
        runner.Respond = (tool, args) => tool == StoreAppReader.PowerShell && FakeRunner.Script(args).Contains("Get-AppxPackage", StringComparison.Ordinal)
            ? (0, ["noise", StoreOutput(StorePayload)])
            : (0, []);
        var launcher = new FakeLauncher();
        var quarantine = new FakeQuarantine();
        var profiles = new FakeProfiles(isProcessUser);
        var engine = new AppsEngine(new Win32AppReader(registry), new StoreAppReader(runner, new FakeNames(), profiles), BloatwareCatalog.Default,
            launcher, runner, quarantine, profiles, (_, _) => Task.CompletedTask);
        return (engine, runner, launcher, quarantine, registry);
    }

    private static AppItem Item(AppsReport report, string name) => report.Apps.Single(a => a.App.Name == name);

    [Fact]
    public async Task Inventory_ReadsWin32AndStore_SkipsComponentsUpdatesAndUnsafeNames()
    {
        var (engine, _, _, _, _) = Create();

        var report = await engine.AnalyzeAsync(CancellationToken.None);

        report.Apps.Select(a => a.App.Name).Should().BeEquivalentTo(
            "Contoso Viewer", "Notes", "Player", "Quiet", "Evil", "Dll tool", "Microsoft.LanguageExperiencePackfr-FR", "Microsoft.Unknown", "Microsoft.Other", "Microsoft Edge WebView2 Runtime", "Microsoft Visual C++ 2015-2022 Redistributable",
            "Chat", "Météo", "Microsoft.VCLibs.140.00", "Microsoft.Windows.ShellExperienceHost", "SpotifyAB.SpotifyMusic", "Microsoft.BingNews");
        var viewer = Item(report, "Contoso Viewer").App;
        viewer.ProductCode.Should().Be(MsiCode);
        viewer.SizeBytes.Should().Be(2048 * 1024);
        viewer.InstallDate.Should().Be(new DateOnly(2025, 1, 2));
        Item(report, "Météo").App.ProvisionedName.Should().Be("Microsoft.BingWeather_4.53.0.0_neutral_~_8wekyb3d8bbwe");
        Item(report, "Météo").App.Publisher.Should().Be("Microsoft Corporation");
        Item(report, "Microsoft.BingNews").App.Scope.Should().Be(AppScope.Machine, "provisionnée sans être installée pour ce compte");
    }

    [Fact]
    public async Task Classification_LockedSystemAndRuntimes_SilentWhenPossible()
    {
        var (engine, _, _, _, _) = Create();
        var report = await engine.AnalyzeAsync(CancellationToken.None);

        Item(report, "Contoso Viewer").Plan!.Method.Should().Be(UninstallMethod.Msi);
        Item(report, "Contoso Viewer").Plan!.Arguments.Should().Be($"/x {MsiCode} /qn /norestart");
        Item(report, "Notes").Plan.Should().Be(new UninstallPlan(UninstallMethod.Silent, @"C:\Program Files (x86)\Notes\unins000.exe",
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"));
        Item(report, "Quiet").Plan.Should().Be(new UninstallPlan(UninstallMethod.Silent, @"C:\Program Files\Quiet\u.exe", "/S"));
        Item(report, "Player").Plan.Should().Be(new UninstallPlan(UninstallMethod.Interactive, @"C:\Program Files\Player\uninstall.exe", "/x"));
        Item(report, "Evil").CanUninstall.Should().BeFalse("désinstalleur hors des dossiers réservés aux administrateurs");
        Item(report, "Chat").CanUninstall.Should().BeFalse("installé pour l'utilisateur seul");
        Item(report, "Microsoft Edge WebView2 Runtime").Risk.Should().Be(RiskLevel.Locked);
        Item(report, "Microsoft Visual C++ 2015-2022 Redistributable").Risk.Should().Be(RiskLevel.Discouraged);
        Item(report, "Microsoft.VCLibs.140.00").CanUninstall.Should().BeFalse();
        Item(report, "Microsoft.Windows.ShellExperienceHost").Risk.Should().Be(RiskLevel.Locked);
        Item(report, "Météo").Should().Match<AppItem>(a => a.IsBloat && a.Risk == RiskLevel.Safe && a.CanUninstall);
        Item(report, "SpotifyAB.SpotifyMusic").Entry!.Reason.Should().Be(BloatReason.Promo);
        Item(report, "Player").Risk.Should().Be(RiskLevel.Safe);
        Item(report, "Dll tool").CanUninstall.Should().BeFalse("un fichier cité dans les arguments n'est pas protégé");
        Item(report, "Microsoft.LanguageExperiencePackfr-FR").Risk.Should().Be(RiskLevel.Locked);
        Item(report, "Microsoft.Unknown").Risk.Should().Be(RiskLevel.Conditional, "paquet Microsoft inconnu : peut être un composant");
        Item(report, "Microsoft.Other").CanUninstall.Should().BeFalse("paquet provisionné seul hors catalogue");
    }

    [Fact]
    public async Task Leftover_ReinstalledFolder_IsDropped()
    {
        var (engine, _, launcher, quarantine, registry) = Create();
        const string folder = @"C:\Program Files (x86)\Notes";
        quarantine.Folders.Add(folder);
        var report = await engine.AnalyzeAsync(CancellationToken.None);
        launcher.OnRun = () => registry.Remove(RegistryHive.LocalMachine, $@"{Key}\Notes_is1", RegistryView.Registry32);
        await engine.UninstallAsync(Item(report, "Notes"), CancellationToken.None);
        engine.IsPendingLeftover(folder).Should().BeTrue();

        registry.Set(RegistryHive.LocalMachine, $@"{Key}\Notes2", "InstallLocation", folder, RegistryView.Registry64);

        engine.IsPendingLeftover(folder).Should().BeFalse("réinstallé dans le même dossier");
        (await engine.AnalyzeAsync(CancellationToken.None)).Leftovers.Should().BeEmpty();
    }

    [Fact]
    public void Store_InvalidTargetSid_FailsClosed()
    {
        var profile = new UserProfile("not-a-sid", "x", "", "", "", IsProcessUser: false);

        var act = () => StoreAppReader.UserArgument(profile);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Uninstall_Msi_WaitsUntilGone()
    {
        var (engine, runner, _, _, registry) = Create();
        var report = await engine.AnalyzeAsync(CancellationToken.None);
        runner.Respond = (tool, _) =>
        {
            registry.Remove(RegistryHive.LocalMachine, $@"{Key}\{MsiCode}", RegistryView.Registry64);
            return (3010, []);
        };

        await engine.UninstallAsync(Item(report, "Contoso Viewer"), CancellationToken.None);

        runner.Calls[^1].Should().Be(("msiexec.exe", $"/x {MsiCode} /qn /norestart"));
    }

    [Fact]
    public async Task Uninstall_StillInstalledAfterWaiting_Throws()
    {
        var (engine, _, launcher, _, _) = Create();
        var report = await engine.AnalyzeAsync(CancellationToken.None);

        var act = () => engine.UninstallAsync(Item(report, "Player"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        launcher.Calls.Should().ContainSingle().Which.Hidden.Should().BeFalse();
    }

    [Fact]
    public async Task Uninstall_RecordsLeftover_ThenQuarantineIsReversible()
    {
        var (engine, _, launcher, quarantine, registry) = Create();
        const string folder = @"C:\Program Files (x86)\Notes";
        quarantine.Folders.Add(folder);
        var report = await engine.AnalyzeAsync(CancellationToken.None);
        launcher.OnRun = () => registry.Remove(RegistryHive.LocalMachine, $@"{Key}\Notes_is1", RegistryView.Registry32);

        await engine.UninstallAsync(Item(report, "Notes"), CancellationToken.None);
        (await engine.AnalyzeAsync(CancellationToken.None)).Leftovers.Should().Equal(folder);

        var fixer = new LeftoversFixer(engine, quarantine);
        var finding = LeftoversFixer.FindingFor(folder);
        fixer.CanFix(finding).Should().BeTrue();
        fixer.CanFix(LeftoversFixer.FindingFor(@"C:\Program Files\Other")).Should().BeFalse("seuls les restes notés sont proposés");
        var entry = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        quarantine.Folders.Should().Equal(@"C:\ProgramData\PCDoc\quarantine\20261121-Notes");
        (await fixer.IsUnchangedSinceAsync(entry, CancellationToken.None)).Should().BeTrue();
        await fixer.UndoAsync(entry, CancellationToken.None);
        quarantine.Folders.Should().Equal(folder);
    }

    [Fact]
    public async Task Leftover_SharedWithAnotherProgram_IsNotRecorded()
    {
        var (engine, _, launcher, quarantine, registry) = Create();
        registry.Set(RegistryHive.LocalMachine, $@"{Key}\Plugin", "DisplayName", "Notes plugin", RegistryView.Registry64);
        registry.Set(RegistryHive.LocalMachine, $@"{Key}\Plugin", "InstallLocation", @"C:\Program Files (x86)\Notes\plugins", RegistryView.Registry64);
        quarantine.Folders.Add(@"C:\Program Files (x86)\Notes");
        var report = await engine.AnalyzeAsync(CancellationToken.None);
        launcher.OnRun = () => registry.Remove(RegistryHive.LocalMachine, $@"{Key}\Notes_is1", RegistryView.Registry32);

        await engine.UninstallAsync(Item(report, "Notes"), CancellationToken.None);

        (await engine.AnalyzeAsync(CancellationToken.None)).Leftovers.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Uninstall_Store_RemovesPackageAndProvisioned(bool isProcessUser, bool expectUserArgument)
    {
        var (engine, runner, _, _, _) = Create(isProcessUser);
        var report = await engine.AnalyzeAsync(CancellationToken.None);

        await engine.UninstallAsync(Item(report, "Météo"), CancellationToken.None);

        var script = FakeRunner.Script(runner.Calls[^1].Args);
        script.Should().Contain("Remove-AppxPackage -Package 'Microsoft.BingWeather_4.53.0.0_x64__8wekyb3d8bbwe'")
            .And.Contain("Remove-AppxProvisionedPackage -Online -PackageName 'Microsoft.BingWeather_4.53.0.0_neutral_~_8wekyb3d8bbwe'");
        script.Contains($"-User '{FakeProfiles.Sid}'", StringComparison.Ordinal).Should().Be(expectUserArgument);
        script.Should().StartWith("$env:PSModulePath = $PSHOME").And.Contain("if (Get-AppxPackage -Name 'Microsoft.BingWeather'");
        FakeRunner.Script(runner.Calls[0].Args).Contains("-User", StringComparison.Ordinal).Should().Be(expectUserArgument);
    }

    [Fact]
    public async Task Uninstall_StoreFailure_Throws()
    {
        var (engine, runner, _, _, _) = Create();
        var report = await engine.AnalyzeAsync(CancellationToken.None);
        runner.Respond = (_, _) => (1, ["Access denied"]);

        var act = () => engine.UninstallAsync(Item(report, "Météo"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Access denied*");
    }

    [Fact]
    public async Task Fixer_RevalidatesAgainstInventory()
    {
        var (engine, _, _, _, _) = Create();
        var report = await engine.AnalyzeAsync(CancellationToken.None);
        var fixer = new UninstallFixer(engine);
        var vc = UninstallFixer.FindingFor(Item(report, "Microsoft Visual C++ 2015-2022 Redistributable"));

        fixer.CanFix(UninstallFixer.FindingFor(Item(report, "Player"))).Should().BeTrue();
        fixer.CanFix(vc).Should().BeTrue();
        fixer.CanFix(vc with { Risk = RiskLevel.Safe }).Should().BeFalse("niveau de risque minimisé");
        fixer.CanFix(UninstallFixer.FindingFor(Item(report, "Microsoft Edge WebView2 Runtime"))).Should().BeFalse();
        fixer.CanFix(UninstallFixer.FindingFor(Item(report, "Evil"))).Should().BeFalse();
        fixer.CanFix(UninstallFixer.FindingFor(Item(report, "Player")) with { Id = "apps.other" }).Should().BeFalse();
        UninstallFixer.FindingFor(Item(report, "Player")).Explanation.Key.Should().Be("Apps.Explanation.Interactive");
        UninstallFixer.FindingFor(Item(report, "Météo")).Explanation.Key.Should().Be("Apps.Explanation.Provisioned");
        UninstallFixer.FindingFor(Item(report, "SpotifyAB.SpotifyMusic")).Explanation.Key.Should().Be("Apps.Reason.Promo");
        fixer.SupportsUndo.Should().BeFalse();
    }

    [Fact]
    public async Task Scanner_ReportsOnlySafeBloatware()
    {
        var (engine, _, _, _, _) = Create();

        var findings = await new AppsScanner(engine).ScanAsync(CancellationToken.None);

        findings.Select(f => f.Title.Args[0]).Should().BeEquivalentTo(["Météo", "SpotifyAB.SpotifyMusic", "Microsoft.BingNews"]);
    }

    [Theory]
    [InlineData("Microsoft.*ImageExtension", "Microsoft.HEIFImageExtension", true)]
    [InlineData("*CandyCrush*", "king.com.CandyCrushSaga", true)]
    [InlineData("Microsoft.NET.*", "Microsoft.NETFramework", false)]
    [InlineData("Microsoft.BingNews", "Microsoft.BingNewsX", false)]
    public void Wildcard_Matches(string pattern, string name, bool expected) =>
        BloatwareCatalog.Wildcard(pattern, name).Should().Be(expected);

    [Fact]
    public void Catalog_LoadsReasonsAndPrefersMostSpecificEntry()
    {
        var catalog = BloatwareCatalog.Default;
        catalog.Entries.Should().Contain(e => e.Reason == BloatReason.Oem);
        var edge = new InstalledApp("win32:64:machine:Edge", AppKind.Win32, AppScope.Machine, "Microsoft Edge WebView2 Runtime");
        catalog.Match(edge)!.Pattern.Should().Be("Microsoft Edge WebView2");
        StoreAppReader.FamilyOf("A.B_1.0_neutral_~_8wekyb3d8bbwe").Should().Be("A.B_8wekyb3d8bbwe");
        UninstallPlanner.Split("\"C:\\a b\\u.exe\" /x").Should().Be((@"C:\a b\u.exe", "/x"));
        UninstallPlanner.Split(@"C:\a b\u.exe /x").Should().Be((@"C:\a b\u.exe", "/x"));
        UninstallPlanner.Split("garbage").Should().BeNull();
    }
}
