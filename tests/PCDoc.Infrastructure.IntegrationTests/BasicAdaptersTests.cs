using PCDoc.Core.Abstractions;
using PCDoc.Infrastructure;

namespace PCDoc.Infrastructure.IntegrationTests;

/// <summary>Tests en lecture seule, exécutés sur le runner Windows de GitHub Actions.</summary>
[Trait("Category", "Windows")]
public sealed class BasicAdaptersTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pcdoc-it-" + Guid.NewGuid().ToString("N"));
    private readonly UserProfileResolver _profiles = new();

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Wmi_OperatingSystem_ReturnsOneRow()
    {
        var rows = new WmiQuery().Query(@"root\cimv2", "SELECT Caption, BuildNumber FROM Win32_OperatingSystem");

        rows.Should().ContainSingle();
        rows[0]["BuildNumber"].Should().NotBeNull();
    }

    [Fact]
    public void Wmi_BadNamespace_ThrowsWmiUnavailable()
    {
        var act = () => new WmiQuery().Query(@"root\does\not\exist", "SELECT * FROM Nothing");

        act.Should().Throw<WmiUnavailableException>();
    }

    [Fact]
    public void Registry_CurrentVersion_HasCurrentBuild()
    {
        new RegistryReader(_profiles)
            .GetValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild")
            .Should().BeOfType<string>().Which.Should().NotBeEmpty();
    }

    [Fact]
    public void Registry_MissingKey_ReturnsEmpty()
    {
        var reader = new RegistryReader(_profiles);

        reader.GetValueNames(RegistryHive.LocalMachine, @"SOFTWARE\PCDoc\Does\Not\Exist").Should().BeEmpty();
        reader.GetValue(RegistryHive.LocalMachine, @"SOFTWARE\PCDoc\Does\Not\Exist", "x").Should().BeNull();
    }

    [Fact]
    public void ProfileResolver_ReturnsInteractiveUser()
    {
        var profile = _profiles.Resolve();

        profile.Sid.Should().StartWith("S-1-5-");
        Directory.Exists(profile.ProfilePath).Should().BeTrue();
        profile.RoamingAppData.Should().StartWith(profile.ProfilePath);
    }

    [Fact]
    public void Registry_TargetUser_ReadsHkeyUsersSid()
    {
        // La ruche du profil cible contient toujours la clé Environment.
        new RegistryReader(_profiles).GetSubKeyNames(RegistryHive.TargetUser, "")
            .Should().Contain(k => k.Equals("Environment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NativeSafety_DllSearchRestricted()
    {
        NativeSafety.RestrictDllSearch().Should().BeTrue();
    }

    [Fact]
    public void EventLog_MissingLog_ReturnsEmpty()
    {
        new EventLogQuery().Query("PCDoc-Does-Not-Exist/Operational", [1], 10).Should().BeEmpty();
    }

    [Fact]
    public void EventLog_System_ReturnsAtMostMax()
    {
        // 6005 / 6006 : démarrage / arrêt du journal des événements, présents sur toute machine.
        var events = new EventLogQuery().Query("System", [6005, 6006, 6013], maxCount: 5);

        events.Count.Should().BeLessThanOrEqualTo(5);
        events.Should().BeInDescendingOrder(e => e.TimeCreated);
    }

    [Fact]
    public void Logger_WritesFileInLogsDir()
    {
        using (var logger = Logging.CreateFileLogger(_tempDir))
        {
            logger.Information("hello {Who}", "runner");
        }

        Directory.GetFiles(_tempDir, "*.log").Should().ContainSingle()
            .Which.Should().Match(p => File.ReadAllText(p).Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public void FileProbe_ExpandsWindir()
    {
        var probe = new FileProbe(_profiles);

        probe.Exists(probe.ExpandEnvironment(@"%windir%\explorer.exe")).Should().BeTrue();
    }
}
