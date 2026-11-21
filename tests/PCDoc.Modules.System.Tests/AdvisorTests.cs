using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.System.Advisor;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.System.Tests;

public class AdvisorTests
{
    private sealed class Conditions(Dictionary<string, bool?> values) : IConditionProbe
    {
        public bool? Check(string condition) => values.TryGetValue(condition, out var v) ? v : null;
    }

    private static AutorunAdvisor Make(params (string Key, bool? Value)[] conditions) =>
        new(AdvisorCatalog.Default, new Conditions(conditions.ToDictionary(c => c.Key, c => c.Value)));

    private static AutorunEntry E(AutorunKind kind, string name, bool microsoft = false, bool enabled = true, string? location = null) =>
        new(kind, name, location ?? (kind == AutorunKind.Service ? $@"HKLM\SYSTEM\CurrentControlSet\Services\{name}" : name), "", @"C:\x.exe",
            enabled, new SignatureInfo(SignatureStatus.Signed, microsoft ? "Microsoft Windows" : "Vendor", false), microsoft)
        {
            ServiceName = kind is AutorunKind.Service or AutorunKind.Driver ? name : null,
            Startup = kind == AutorunKind.Startup ? new StartupToggleTarget(RegistryHive.TargetUser, "Run", name) : null,
        };

    [Fact]
    public void RunOnceStartup_WithoutToggleTarget_IsLocked() =>
        Make().Advise(E(AutorunKind.Startup, "Setup") with { Startup = null }).Level.Should().Be(RiskLevel.Locked);

    [Fact]
    public void CriticalService_IsLocked() =>
        Make().Advise(E(AutorunKind.Service, "wuauserv", microsoft: true)).Level.Should().Be(RiskLevel.Locked);

    [Fact]
    public void Spooler_NoPrinter_IsSafe()
    {
        var advice = Make(("no-printer", true)).Advise(E(AutorunKind.Service, "Spooler", microsoft: true));

        advice.Level.Should().Be(RiskLevel.Safe);
        advice.ReasonKey.Should().Be("Advice.Spooler");
        advice.ConditionMet.Should().BeTrue();
    }

    [Fact]
    public void Spooler_WithPrinter_IsDiscouraged() =>
        Make(("no-printer", false)).Advise(E(AutorunKind.Service, "Spooler", microsoft: true)).Level.Should().Be(RiskLevel.Discouraged);

    [Fact]
    public void Xbox_Unverifiable_IsDiscouraged() =>
        Make().Advise(E(AutorunKind.Service, "XblGameSave", microsoft: true)).Level.Should().Be(RiskLevel.Discouraged);

    [Fact]
    public void UnknownThirdPartyStartup_IsSafe() =>
        Make().Advise(E(AutorunKind.Startup, "Discord")).Level.Should().Be(RiskLevel.Safe);

    [Fact]
    public void UnknownMicrosoftStartup_IsDiscouraged() =>
        Make().Advise(E(AutorunKind.Startup, "SomethingMs", microsoft: true)).Level.Should().Be(RiskLevel.Discouraged);

    [Fact]
    public void EdgeAutoLaunch_PrefixMatch_IsSafe() =>
        Make().Advise(E(AutorunKind.Startup, "MicrosoftEdgeAutoLaunch_0123ABCD", microsoft: true)).Level.Should().Be(RiskLevel.Safe);

    [Fact]
    public void SecurityHealthStartup_IsLocked() =>
        Make().Advise(E(AutorunKind.Startup, "SecurityHealth", microsoft: true)).Level.Should().Be(RiskLevel.Locked);

    [Fact]
    public void UnknownService_IsDiscouraged() =>
        Make().Advise(E(AutorunKind.Service, "rgbsvc")).Level.Should().Be(RiskLevel.Discouraged);

    [Fact]
    public void Drivers_AreLocked() =>
        Make().Advise(E(AutorunKind.Driver, "disk", microsoft: true)).Level.Should().Be(RiskLevel.Locked);

    [Fact]
    public void TelemetryTask_IsSafe() =>
        Make().Advise(E(AutorunKind.ScheduledTask, @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", true))
            .Level.Should().Be(RiskLevel.Safe);

    [Fact]
    public void WindowsUpdateTask_IsLocked() =>
        Make().Advise(E(AutorunKind.ScheduledTask, @"\Microsoft\Windows\WindowsUpdate\Scheduled Start", true)).Level.Should().Be(RiskLevel.Locked);

    [Fact]
    public void DisabledItem_ReEnable_IsSafeUnlessLocked()
    {
        var advice = Make().Advise(E(AutorunKind.Service, "rgbsvc", enabled: false));

        advice.Action.Should().Be(ToggleAction.Enable);
        advice.Level.Should().Be(RiskLevel.Safe);
    }

    [Fact]
    public void ServiceNameComparison_IsCaseInsensitive() =>
        Make().Advise(E(AutorunKind.Service, "WUAUSERV", microsoft: true)).Level.Should().Be(RiskLevel.Locked);

    [Fact]
    public void CatalogFile_ParsesFromOutputFolder()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Definitions", "advisor.json");

        var catalog = AdvisorCatalog.Load(File.ReadAllText(path));

        catalog.Version.Should().BeGreaterThan(0);
        catalog.LockedServices.Should().Contain("WinDefend");
    }

    [Fact]
    public void ConditionProbe_OnlyVirtualPrinters_MeansNoPrinter()
    {
        var wmi = new FakeWmiQuery().Add(@"root\cimv2", "Win32_Printer",
            Row.Of(("Name", "Microsoft Print to PDF")), Row.Of(("Name", "Microsoft XPS Document Writer")), Row.Of(("Name", "OneNote (Desktop)")));

        new ConditionProbe(wmi, new FakeRegistryReader()).Check("no-printer").Should().BeTrue();
    }

    [Fact]
    public void ConditionProbe_RealPrinter_MeansPrinter()
    {
        var wmi = new FakeWmiQuery().Add(@"root\cimv2", "Win32_Printer", Row.Of(("Name", "HP LaserJet M110")));

        new ConditionProbe(wmi, new FakeRegistryReader()).Check("no-printer").Should().BeFalse();
    }

    [Fact]
    public void ConditionProbe_WmiDown_IsUnknown()
    {
        var wmi = new FakeWmiQuery().ThrowOnNamespace(@"root\cimv2");

        new ConditionProbe(wmi, new FakeRegistryReader()).Check("no-bluetooth").Should().BeNull();
    }

    [Fact]
    public void ConditionProbe_UnknownCondition_IsUnknown() =>
        new ConditionProbe(new FakeWmiQuery(), new FakeRegistryReader()).Check("no-xbox").Should().BeNull();
}
