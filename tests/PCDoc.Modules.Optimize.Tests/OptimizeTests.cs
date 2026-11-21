using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.Optimize.Tests;

public class OptimizeTests
{
    private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string High = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private sealed class Writer(FakeRegistryReader registry) : IRegistryValueWriter, IRegistryWriter
    {
        public void SetValue(RegistryHive hive, string path, string name, object? value) => registry.Set(hive, path, name, value, RegistryView.Registry64);

        public void SetBinary(RegistryHive hive, string path, string name, byte[] value) => throw new NotSupportedException();

        public void DeleteValue(RegistryHive hive, string path, string name) => registry.Set(hive, path, name, null, RegistryView.Registry64);

        public string FullPath(RegistryHive hive, string path) => $@"HKEY_USERS\S-1-5-21-1\{path}";
    }

    private sealed class Backups : IBackupStore
    {
        public List<string> Saved { get; } = [];

        public string SaveRegistryBackup(string name, string regFileContent)
        {
            Saved.Add(regFileContent);
            return name;
        }
    }

    private sealed class Powercfg : IProcessRunner
    {
        public string Active { get; set; } = Balanced;

        public List<string> Calls { get; } = [];

        public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            Calls.Add(arguments);
            if (arguments == "/list")
            {
                onOutput?.Invoke($"GUID du mode de gestion de l'alimentation : {Balanced}  (Utilisation normale) *");
                onOutput?.Invoke($"GUID du mode de gestion de l'alimentation : {High}  (Performances élevées)");
            }
            else if (arguments == "/getactivescheme")
            {
                onOutput?.Invoke($"GUID du mode de gestion de l'alimentation : {Active}  (x)");
            }
            else if (arguments.StartsWith("/setactive ", StringComparison.Ordinal))
            {
                Active = arguments[11..];
            }

            return Task.FromResult(0);
        }
    }

    private static (OptimizeEngine Engine, TweakFixer Fixer, FakeRegistryReader Registry, Powercfg Power, Backups Backups) Create(bool laptop)
    {
        var registry = new FakeRegistryReader();
        registry.Set(RegistryHive.TargetUser, @"Control Panel\Desktop", "MenuShowDelay", "400", RegistryView.Registry64);
        registry.Set(RegistryHive.LocalMachine, OptimizeEngine.PowerKey, "HibernateEnabled", 1, RegistryView.Registry64);
        var wmi = new FakeWmiQuery();
        if (laptop)
        {
            wmi.Add(@"root\cimv2", "Win32_Battery", new Dictionary<string, object?> { ["Name"] = "Battery" });
        }

        var power = new Powercfg();
        var writer = new Writer(registry);
        var engine = new OptimizeEngine(TweakCatalog.Default, registry, writer, power, wmi);
        var backups = new Backups();
        return (engine, new TweakFixer(engine, writer, backups, () => new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)), registry, power, backups);
    }

    private static async Task<Finding> FindingAsync(OptimizeEngine engine, string id) =>
        TweakFixer.FindingFor((await engine.AnalyzeAsync(CancellationToken.None)).Single(s => s.Tweak.Id == id));

    [Fact]
    public async Task States_AndDesktopCondition()
    {
        var (laptop, _, _, _, _) = Create(laptop: true);
        var (desktop, _, _, _, _) = Create(laptop: false);

        var states = await laptop.AnalyzeAsync(CancellationToken.None);
        states.Single(s => s.Tweak.Id == "power-balanced").Applied.Should().BeTrue();
        states.Single(s => s.Tweak.Id == "power-high").Risk.Should().Be(RiskLevel.Conditional);
        (await desktop.AnalyzeAsync(CancellationToken.None)).Single(s => s.Tweak.Id == "power-high").Risk.Should().Be(RiskLevel.Safe);
        states.Single(s => s.Tweak.Id == "hibernate-off").Applied.Should().BeFalse();
        TweakCatalog.Default.Tweaks.Should().OnlyContain(t => t.Profiles.All(p => TweakCatalog.ProfileIds.Contains(p)));
    }

    [Fact]
    public async Task RegistryTweak_AppliesAndUndoesExactly()
    {
        var (engine, fixer, registry, _, backups) = Create(laptop: true);
        var finding = await FindingAsync(engine, "menu-delay");

        var entry = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        registry.GetValue(RegistryHive.TargetUser, @"Control Panel\Desktop", "MenuShowDelay", RegistryView.Registry64).Should().Be("100");
        backups.Saved.Single().Should().Contain("\"MenuShowDelay\"=\"400\"");
        (await fixer.IsUnchangedSinceAsync(entry, CancellationToken.None)).Should().BeTrue();
        await fixer.UndoAsync(entry, CancellationToken.None);
        registry.GetValue(RegistryHive.TargetUser, @"Control Panel\Desktop", "MenuShowDelay", RegistryView.Registry64).Should().Be("400");
    }

    [Fact]
    public async Task MissingValue_IsDeletedOnUndo()
    {
        var (engine, fixer, registry, _, _) = Create(laptop: true);
        var finding = await FindingAsync(engine, "game-dvr-off");

        var entry = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);
        await fixer.UndoAsync(entry, CancellationToken.None);

        registry.GetValue(RegistryHive.TargetUser, @"System\GameConfigStore", "GameDVR_Enabled", RegistryView.Registry64).Should().BeNull();
    }

    [Fact]
    public async Task PowerPlan_AndHibernate_AreReversible()
    {
        var (engine, fixer, _, power, _) = Create(laptop: false);
        var high = await FindingAsync(engine, "power-high");
        var entry = await fixer.PrepareAsync(high, CancellationToken.None);
        await fixer.ApplyAsync(high, CancellationToken.None);
        power.Active.Should().Be(High);
        await fixer.UndoAsync(entry, CancellationToken.None);
        power.Active.Should().Be(Balanced);

        var hibernate = await FindingAsync(engine, "hibernate-off");
        await fixer.PrepareAsync(hibernate, CancellationToken.None);
        await fixer.ApplyAsync(hibernate, CancellationToken.None);
        power.Calls.Should().Contain("/hibernate off");
    }

    [Fact]
    public async Task Refuses_UnderstatedRisk_AndForgedJournal()
    {
        var (engine, fixer, _, _, _) = Create(laptop: true);
        var high = await FindingAsync(engine, "power-high");

        fixer.CanFix(high with { Risk = RiskLevel.Safe }).Should().BeFalse("portable : 🟠");
        var forged = UndoEntry.Create("optimize.menu-delay", TweakFixer.Kind, "{\"Tweak\":\"menu-delay\",\"Values\":[{\"Dword\":5}]}", "x", []);
        var act = () => fixer.UndoAsync(forged, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>("type de valeur différent du catalogue");
    }

    [Fact]
    public async Task Scanner_OffersOnlyBilanTweaks()
    {
        var (engine, _, _, _, _) = Create(laptop: true);

        var findings = await new OptimizeScanner(engine).ScanAsync(CancellationToken.None);

        findings.Select(f => f.Id).Should().BeEquivalentTo(["optimize.game-dvr-off", "optimize.startup-delay-off"]);
    }
}
