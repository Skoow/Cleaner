using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.System.Advisor;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Fixes;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.System.Tests;

public class AutorunToggleFixerTests
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    internal sealed class FakeSystem : IRegistryWriter, IServiceControl, ITaskControl, IBackupStore
    {
        public FakeRegistryReader Registry { get; } = new();

        public Dictionary<string, ServiceStartMode> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, bool> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Name, string Content)> Backups { get; } = [];

        public void SetBinary(RegistryHive hive, string path, string name, byte[] value) => Registry.Set(hive, path, name, value);

        public void DeleteValue(RegistryHive hive, string path, string name) => Registry.Set(hive, path, name, null);

        public string FullPath(RegistryHive hive, string path) =>
            (hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : @"HKEY_USERS\S-1-5-21-1000") + @"\" + path;

        public ServiceStartMode GetStartMode(string serviceName) => Services[serviceName];

        public void SetStartMode(string serviceName, ServiceStartMode mode) => Services[serviceName] = mode;

        public bool? IsEnabled(string taskPath) => Tasks.TryGetValue(taskPath, out var v) ? v : null;

        public void SetEnabled(string taskPath, bool enabled) => Tasks[taskPath] = enabled;

        public string SaveRegistryBackup(string name, string regFileContent)
        {
            Backups.Add((name, regFileContent));
            return @"C:\ProgramData\PCDoc\backup\" + name;
        }
    }

    private sealed class NoConditions : IConditionProbe
    {
        public bool? Check(string condition) => null;
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static (AutorunToggleFixer Fixer, FakeSystem System) Make()
    {
        var sys = new FakeSystem();
        var advisor = new AutorunAdvisor(AdvisorCatalog.Default, new NoConditions());
        return (new AutorunToggleFixer(sys.Registry, sys, sys, sys, sys, () => Now, advisor), sys);
    }

    private static Finding FindingFor(ToggleRequest request) =>
        new("system.toggle.x", "system", Severity.Info, new LocalizedText("T"), new LocalizedText("E"), "", true, RiskLevel.Safe,
            FixData: request.Serialize());

    private static readonly StartupToggleTarget Discord = new(RegistryHive.TargetUser, "Run", "Discord");

    [Fact]
    public async Task Startup_Disable_WritesDisabledByte_AndUndoRestoresAbsence()
    {
        var (fixer, sys) = Make();
        var finding = FindingFor(ToggleRequest.ForStartup("Discord", Discord, ToggleAction.Disable));

        var undo = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        var written = (byte[])sys.Registry.GetValue(RegistryHive.TargetUser, $@"{Approved}\Run", "Discord")!;
        written.Should().HaveCount(12);
        written[0].Should().Be(0x03);
        (await fixer.IsUnchangedSinceAsync(undo, CancellationToken.None)).Should().BeTrue();

        await fixer.UndoAsync(undo, CancellationToken.None);
        sys.Registry.GetValue(RegistryHive.TargetUser, $@"{Approved}\Run", "Discord").Should().BeNull();
    }

    [Fact]
    public async Task Startup_Prepare_SavesRegBackup()
    {
        var (fixer, sys) = Make();
        sys.Registry.Set(RegistryHive.TargetUser, $@"{Approved}\Run", "Discord", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        await fixer.PrepareAsync(FindingFor(ToggleRequest.ForStartup("Discord", Discord, ToggleAction.Disable)), CancellationToken.None);

        var (_, content) = sys.Backups.Single();
        content.Should().StartWith("Windows Registry Editor Version 5.00");
        content.Should().Contain(@"[HKEY_USERS\S-1-5-21-1000\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run]");
        content.Should().Contain("\"Discord\"=hex:02,00,00,00,00,00,00,00,00,00,00,00");
    }

    [Fact]
    public async Task Startup_Enable_WritesEnabledByte()
    {
        var (fixer, sys) = Make();
        sys.Registry.Set(RegistryHive.TargetUser, $@"{Approved}\Run", "Discord", new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 });
        var finding = FindingFor(ToggleRequest.ForStartup("Discord", Discord, ToggleAction.Enable));

        await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        ((byte[])sys.Registry.GetValue(RegistryHive.TargetUser, $@"{Approved}\Run", "Discord")!)[0].Should().Be(0x02);
    }

    [Fact]
    public async Task Service_Disable_AndUndo_RestoresOriginalMode()
    {
        var (fixer, sys) = Make();
        sys.Services["Spooler"] = ServiceStartMode.AutomaticDelayed;
        var finding = FindingFor(ToggleRequest.ForService("Spooler", ToggleAction.Disable)) with { Risk = RiskLevel.Discouraged };

        var undo = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);
        sys.Services["Spooler"].Should().Be(ServiceStartMode.Disabled);

        await fixer.UndoAsync(undo, CancellationToken.None);
        sys.Services["Spooler"].Should().Be(ServiceStartMode.AutomaticDelayed);
    }

    [Fact]
    public async Task Service_Enable_WithoutHistory_SetsManual()
    {
        var (fixer, sys) = Make();
        sys.Services["rgbsvc"] = ServiceStartMode.Disabled;
        var finding = FindingFor(ToggleRequest.ForService("rgbsvc", ToggleAction.Enable)) with { Risk = RiskLevel.Discouraged };

        await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        sys.Services["rgbsvc"].Should().Be(ServiceStartMode.Manual);
    }

    [Fact]
    public async Task Service_ChangedSince_IsDetected()
    {
        var (fixer, sys) = Make();
        sys.Services["Spooler"] = ServiceStartMode.Automatic;
        var finding = FindingFor(ToggleRequest.ForService("Spooler", ToggleAction.Disable)) with { Risk = RiskLevel.Discouraged };
        var undo = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        sys.Services["Spooler"] = ServiceStartMode.Manual;

        (await fixer.IsUnchangedSinceAsync(undo, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Task_Disable_AndUndo()
    {
        var (fixer, sys) = Make();
        const string Path = @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator";
        sys.Tasks[Path] = true;
        var finding = FindingFor(ToggleRequest.ForTask(Path, ToggleAction.Disable));

        var undo = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);
        sys.Tasks[Path].Should().BeFalse();

        await fixer.UndoAsync(undo, CancellationToken.None);
        sys.Tasks[Path].Should().BeTrue();
    }

    [Fact]
    public void CanFix_OnlyFindingsWithToggleData()
    {
        var (fixer, _) = Make();

        fixer.CanFix(FindingFor(ToggleRequest.ForTask(@"\x", ToggleAction.Disable)) with { Risk = RiskLevel.Discouraged }).Should().BeTrue();
        fixer.CanFix(new Finding("system.disk-low.c", "system", Severity.Warning, new("T"), new("E"), "")).Should().BeFalse();
        fixer.CanFix(new Finding("system.x", "system", Severity.Info, new("T"), new("E"), "", FixData: "{ not json")).Should().BeFalse();
    }

    [Fact]
    public async Task LockedService_IsRefused_EvenWithCraftedFinding()
    {
        var (fixer, sys) = Make();
        sys.Services["wuauserv"] = ServiceStartMode.Manual;
        var finding = FindingFor(ToggleRequest.ForService("wuauserv", ToggleAction.Disable));

        fixer.CanFix(finding).Should().BeFalse();
        var act = () => fixer.ApplyAsync(finding, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
        sys.Services["wuauserv"].Should().Be(ServiceStartMode.Manual);
    }

    [Fact]
    public async Task ForgedUndoEntry_TargetingLockedService_IsRefused()
    {
        var (fixer, sys) = Make();
        sys.Services["wuauserv"] = ServiceStartMode.Manual;
        // Entrée falsifiée : « annuler une réactivation » = remettre Windows Update en Désactivé.
        var payload = "{\"Request\":{\"Kind\":2,\"Action\":1,\"Name\":\"wuauserv\"},\"Before\":{\"ServiceMode\":5}}";
        var forged = new UndoEntry("x", "f", AutorunToggleFixer.Kind, payload, "Undo.Enabled", ["harmless"], Now, UndoStatus.Applied);

        (await fixer.IsUnchangedSinceAsync(forged, CancellationToken.None)).Should().BeFalse();
        var act = () => fixer.UndoAsync(forged, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
        sys.Services["wuauserv"].Should().Be(ServiceStartMode.Manual);
    }

    [Fact]
    public void Finding_ClaimingSaferThanCatalog_IsRefused()
    {
        var (fixer, _) = Make();
        // Service inconnu = 🔴 ; un problème qui prétend « Sûr » est refusé.
        fixer.CanFix(FindingFor(ToggleRequest.ForService("rgbsvc", ToggleAction.Disable))).Should().BeFalse();
        fixer.CanFix(FindingFor(ToggleRequest.ForService("rgbsvc", ToggleAction.Disable)) with { Risk = RiskLevel.Discouraged })
            .Should().BeTrue();
    }

    [Fact]
    public void ToggleRequest_RoundTrips() =>
        ToggleRequest.Parse(ToggleRequest.ForStartup("Discord", Discord, ToggleAction.Disable).Serialize())
            .Should().Be(ToggleRequest.ForStartup("Discord", Discord, ToggleAction.Disable));
}
