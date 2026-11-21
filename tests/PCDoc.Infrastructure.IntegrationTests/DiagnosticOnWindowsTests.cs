using PCDoc.Core.Abstractions;
using PCDoc.Modules.Diagnostic;

namespace PCDoc.Infrastructure.IntegrationTests;

/// <summary>Le diagnostic contre le vrai Windows du runner : aucune section ne doit planter.</summary>
[Trait("Category", "Windows")]
public class DiagnosticOnWindowsTests
{
    private static DiagnosticReport Collect()
    {
        var profiles = new UserProfileResolver();
        return new DiagnosticCollector(new EventLogQuery(), new WmiQuery(), new RegistryReader(profiles), new SystemClock()).Collect();
    }

    [Fact]
    public void Collect_AllSectionsReadable()
    {
        var report = Collect();

        report.Disks.Available.Should().BeTrue();
        report.Disks.Value.Should().NotBeEmpty();
        report.Devices.Available.Should().BeTrue();
        report.BlueScreens.Available.Should().BeTrue();
        report.AppCrashes.Available.Should().BeTrue();
        report.OldDrivers.Available.Should().BeTrue();
    }

    [Fact]
    public void ReliabilityCounters_AssociatorQuery_DoesNotThrow()
    {
        var wmi = new WmiQuery();
        var disk = wmi.Query(@"root\Microsoft\Windows\Storage", "SELECT ObjectId FROM MSFT_PhysicalDisk")[0];
        var id = ((string)disk["ObjectId"]!).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        var act = () => wmi.Query(@"root\Microsoft\Windows\Storage",
            $"ASSOCIATORS OF {{MSFT_PhysicalDisk.ObjectId=\"{id}\"}} WHERE AssocClass = MSFT_PhysicalDiskToStorageReliabilityCounter ResultClass = MSFT_StorageReliabilityCounter");

        act.Should().NotThrow();
    }
}
