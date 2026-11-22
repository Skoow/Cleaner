using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;
using PCDoc.Modules.Optimize;
using PCDoc.Modules.System.Tests;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.Security.Tests;

public class SecurityTests
{
    private const string Ifeo = SecurityEngine.IfeoPath;

    private sealed class Writer(FakeRegistryReader registry) : IRegistryValueWriter, IRegistryWriter
    {
        public void SetValue(RegistryHive hive, string path, string name, object? value) => registry.Set(hive, path, name, value, RegistryView.Registry64);

        public void SetBinary(RegistryHive hive, string path, string name, byte[] value) => throw new NotSupportedException();

        public void DeleteValue(RegistryHive hive, string path, string name) => registry.Set(hive, path, name, null, RegistryView.Registry64);

        public string FullPath(RegistryHive hive, string path) => @"HKEY_LOCAL_MACHINE\" + path;
    }

    private sealed class Runner : IProcessRunner
    {
        public bool Smb1 { get; set; } = true;

        public List<string> Calls { get; } = [];

        public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            Calls.Add($"{system32Tool} {arguments}");
            if (arguments.Contains("/Get-FeatureInfo", StringComparison.Ordinal))
            {
                onOutput?.Invoke("Feature Name : SMB1Protocol");
                onOutput?.Invoke(Smb1 ? "State : Enabled" : "State : Disabled");
            }
            else if (arguments.Contains("/Disable-Feature", StringComparison.Ordinal))
            {
                Smb1 = false;
            }
            else if (arguments.Contains("/Enable-Feature", StringComparison.Ordinal))
            {
                Smb1 = true;
            }

            return Task.FromResult(arguments.Contains("Disable-Feature", StringComparison.Ordinal) ? 3010 : 0);
        }
    }

    private sealed class Defender : IDefenderCommand
    {
        public List<string> Calls { get; } = [];

        public Task<int> RunAsync(string arguments, Action<string>? onOutput, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return Task.FromResult(0);
        }
    }

    private sealed class Drives : IRemovableDrives
    {
        public Dictionary<string, RootItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<RemovableDrive> List() => [new(@"E:\", "CLE")];

        public bool IsRemovableRoot(string root) => root.Equals(@"E:\", StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<RootItem> RootItems(string root) => [.. Items.Values];

        public void SetAttributes(string path, FileAttributes attributes) => Items[path] = Items[path] with { Attributes = attributes };

        public void Rename(string path, string newPath)
        {
            Items.Remove(path, out var item).Should().BeTrue();
            Items[newPath] = item! with { Path = newPath };
        }
    }

    private sealed record Setup(SecurityEngine Engine, FakeRegistryReader Registry, Runner Runner, Defender Defender, Drives Drives, FakeWmiQuery Wmi, Writer Writer);

    private static Setup Create(bool infectedUsb = true, bool firewallOff = true)
    {
        var registry = new FakeRegistryReader();
        registry.Set(RegistryHive.LocalMachine, SecurityEngine.WinlogonPath, "Userinit", @"C:\Windows\system32\userinit.exe,C:\Users\Public\evil.exe", RegistryView.Registry64);
        registry.Set(RegistryHive.LocalMachine, SecurityEngine.WinlogonPath, "Shell", "Explorer.exe", RegistryView.Registry64);
        registry.Set(RegistryHive.LocalMachine, $@"{Ifeo}\sethc.exe", "Debugger", @"C:\Windows\System32\cmd.exe", RegistryView.Registry64);
        registry.Set(RegistryHive.LocalMachine, $@"{Ifeo}\notepad.exe", "UseFilter", 1, RegistryView.Registry64);
        registry.Set(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 2, RegistryView.Registry64);

        var wmi = new FakeWmiQuery()
            .Add(@"root\SecurityCenter2", "AntiVirusProduct", new Dictionary<string, object?> { ["displayName"] = "Windows Defender", ["productState"] = 397568L })
            .Add(@"root\Microsoft\Windows\Defender", "MSFT_MpComputerStatus", new Dictionary<string, object?>
            {
                ["AntivirusEnabled"] = true, ["RealTimeProtectionEnabled"] = true, ["AntivirusSignatureAge"] = 9u, ["QuickScanAge"] = 2u, ["IsTamperProtected"] = true,
            })
            .Add(@"root\StandardCimv2", "MSFT_NetFirewallProfile",
                new Dictionary<string, object?> { ["Name"] = "Domain", ["Enabled"] = (ushort)1 },
                new Dictionary<string, object?> { ["Name"] = "Public", ["Enabled"] = (ushort)(firewallOff ? 0 : 1) });

        var files = new FakeFileProbe()
            .WithContent(@"C:\Windows\System32\drivers\etc\hosts", global::System.Text.Encoding.UTF8.GetBytes(
                "# comment\n127.0.0.1 localhost\n203.0.113.9 www.malwarebytes.com\n0.0.0.0 example.org\n"));
        var drives = new Drives();
        drives.Items[@"E:\Photos"] = new RootItem(@"E:\Photos", true, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
        drives.Items[@"E:\System Volume Information"] = new RootItem(@"E:\System Volume Information", true, FileAttributes.Directory | FileAttributes.Hidden);
        if (infectedUsb)
        {
            drives.Items[@"E:\Photos.lnk"] = new RootItem(@"E:\Photos.lnk", false, FileAttributes.Archive);
            files.WithContent(@"E:\Photos.lnk", LnkParserTests.BuildLnk(@"C:\Windows\System32\wscript.exe", "//e:vbscript x.dat"));
        }

        var runner = new Runner();
        var writer = new Writer(registry);
        var settings = new OptimizeEngine(SecurityModule.Settings, registry, writer, runner, wmi);
        var defender = new Defender();
        var engine = new SecurityEngine(wmi, registry, writer, runner, defender, files, drives, settings, @"C:\Windows");
        return new Setup(engine, registry, runner, defender, drives, wmi, writer);
    }

    private static async Task<Finding> Find(SecurityEngine engine, string id) =>
        (await engine.AnalyzeAsync(CancellationToken.None)).Findings.Single(f => f.Id == id);

    [Fact]
    public async Task Audit_FindsHijacksAndWeakSettings_ButNotHealthyOnes()
    {
        var s = Create();

        var report = await s.Engine.AnalyzeAsync(CancellationToken.None);

        report.Findings.Select(f => f.Id).Should().BeEquivalentTo(
            "security.defender.signatures", "security.firewall.Public", "security.smb1", "security.ifeo.sethc.exe", "security.userinit",
            "security.hosts", "security.autorun-off", "security.usb.E");
        report.Findings.Single(f => f.Id == "security.hosts").Severity.Should().Be(Severity.Warning);
        report.Antivirus.Should().ContainSingle().Which.Should().Be(new AntivirusInfo("Windows Defender", true, true, true));
        report.Usb.Single().HiddenFolders.Should().ContainSingle().Which.Path.Should().Be(@"E:\Photos");
    }

    [Fact]
    public async Task ThirdPartyAntivirus_PutsDefenderInPassiveMode_NoAlert()
    {
        var s = Create();
        s.Wmi.Replace(@"root\SecurityCenter2", "AntiVirusProduct",
            new Dictionary<string, object?> { ["displayName"] = "Windows Defender", ["productState"] = 393472L },
            new Dictionary<string, object?> { ["displayName"] = "ESET Security", ["productState"] = 266240L });

        var report = await s.Engine.AnalyzeAsync(CancellationToken.None);

        report.Defender.Active.Should().BeFalse();
        report.Findings.Should().NotContain(f => f.Id.StartsWith("security.defender", StringComparison.Ordinal) || f.Id == "security.av.none");
    }

    [Fact]
    public async Task Userinit_AndIfeo_AreRepairedAndUndone()
    {
        var s = Create();
        var fixer = new RegistryRepairFixer(s.Engine);
        var userinit = await Find(s.Engine, "security.userinit");
        var ifeo = await Find(s.Engine, "security.ifeo.sethc.exe");
        ifeo.Risk.Should().Be(RiskLevel.Conditional);
        fixer.CanFix(ifeo with { Risk = RiskLevel.Safe }).Should().BeFalse();

        var e1 = await fixer.PrepareAsync(userinit, CancellationToken.None);
        await fixer.ApplyAsync(userinit, CancellationToken.None);
        var e2 = await fixer.PrepareAsync(ifeo, CancellationToken.None);
        await fixer.ApplyAsync(ifeo, CancellationToken.None);

        s.Engine.CurrentUserinit().Should().Be(@"C:\Windows\system32\userinit.exe,");
        s.Engine.IfeoDebuggers().Should().BeEmpty();
        (await fixer.IsUnchangedSinceAsync(e1, CancellationToken.None)).Should().BeTrue();
        await fixer.UndoAsync(e2, CancellationToken.None);
        s.Engine.IfeoDebuggers().Should().ContainSingle();
        fixer.CanFix(userinit).Should().BeFalse("déjà réparé");

        var forged = UndoEntry.Create("x", "security.repair",
            """{"Target":{"Kind":"ifeo","Root":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run","Exe":"evil"},"Previous":"x"}""", "t", []);
        var act = () => fixer.UndoAsync(forged, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>("le journal ne peut viser que les deux valeurs prévues");
    }

    [Fact]
    public async Task Firewall_Smb1_AndSettings_AreReversible()
    {
        var s = Create();
        var firewall = new FirewallFixer(s.Engine);
        var finding = await Find(s.Engine, "security.firewall.Public");
        var entry = await firewall.PrepareAsync(finding, CancellationToken.None);
        await firewall.ApplyAsync(finding, CancellationToken.None);
        await firewall.UndoAsync(entry, CancellationToken.None);
        s.Runner.Calls.Should().Contain(["netsh.exe advfirewall set publicprofile state on", "netsh.exe advfirewall set publicprofile state off"]);

        var feature = new FeatureFixer(s.Engine);
        var smb = await Find(s.Engine, "security.smb1");
        var e = await feature.PrepareAsync(smb, CancellationToken.None);
        await feature.ApplyAsync(smb, CancellationToken.None);
        s.Runner.Smb1.Should().BeFalse();
        await feature.UndoAsync(e, CancellationToken.None);
        s.Runner.Smb1.Should().BeTrue();

        var settings = new TweakFixer(s.Engine.Settings, s.Writer, new NoBackup(), () => DateTimeOffset.UnixEpoch, SecurityModule.SettingKind);
        var autorun = await Find(s.Engine, "security.autorun-off");
        settings.UndoKind.Should().Be(SecurityModule.SettingKind);
        var se = await settings.PrepareAsync(autorun, CancellationToken.None);
        await settings.ApplyAsync(autorun, CancellationToken.None);
        (await s.Engine.AnalyzeAsync(CancellationToken.None)).Findings.Should().NotContain(f => f.Id == "security.autorun-off");
        await settings.UndoAsync(se, CancellationToken.None);
    }

    private sealed class NoBackup : IBackupStore
    {
        public string SaveRegistryBackup(string name, string regFileContent) => name;
    }

    [Fact]
    public async Task Usb_ShortcutVirus_IsNeutralizedAndUndone()
    {
        var s = Create();
        var fixer = new UsbFixer(s.Engine);
        var finding = await Find(s.Engine, "security.usb.E");

        var entry = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        s.Drives.Items.Keys.Should().Contain(@"E:\Photos.lnk.pcdoc-disabled").And.NotContain(@"E:\Photos.lnk");
        s.Drives.Items[@"E:\Photos"].Attributes.Should().Be(FileAttributes.Directory);
        s.Drives.Items[@"E:\System Volume Information"].Attributes.HasFlag(FileAttributes.Hidden).Should().BeTrue();
        (await fixer.IsUnchangedSinceAsync(entry, CancellationToken.None)).Should().BeTrue();

        await fixer.UndoAsync(entry, CancellationToken.None);
        s.Drives.Items.Keys.Should().Contain(@"E:\Photos.lnk");
        s.Drives.Items[@"E:\Photos"].Attributes.HasFlag(FileAttributes.Hidden).Should().BeTrue();
    }

    [Fact]
    public async Task Usb_HiddenFoldersAlone_AreNotAVirus_AndScansAreValidated()
    {
        var s = Create(infectedUsb: false, firewallOff: false);

        (await s.Engine.AnalyzeAsync(CancellationToken.None)).Findings.Should().NotContain(f => f.Id.StartsWith("security.usb", StringComparison.Ordinal));
        await s.Engine.ScanAsync(3, @"E:\", null, CancellationToken.None);
        s.Defender.Calls.Should().Equal("-Scan -ScanType 3 -File \"E:\\\"");
        var act = () => s.Engine.ScanAsync(3, @"C:\", null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>("seuls les lecteurs amovibles");
        var act2 = () => s.Engine.ScanAsync(3, "E:\\\" -DisableRemediation \"", null, CancellationToken.None);
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Defender_UpdatesSignatures_AndScannerReportsAll()
    {
        var s = Create();
        var finding = await Find(s.Engine, "security.defender.signatures");

        await new DefenderFixer(s.Engine).ApplyAsync(finding, CancellationToken.None);

        s.Defender.Calls.Should().Equal("-SignatureUpdate");
        (await new SecurityScanner(s.Engine).ScanAsync(CancellationToken.None)).Should().HaveCount(8);
        SecurityModule.Settings.Tweaks.Should().OnlyContain(t => t.Module == "security" && t.TextPrefix == "Security");
    }
}
