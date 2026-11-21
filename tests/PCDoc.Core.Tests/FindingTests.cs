using PCDoc.Core.Findings;

namespace PCDoc.Core.Tests;

public class FindingTests
{
    private static Finding Sample() => new(
        "system.disk-low.C", "system", Severity.Warning,
        new LocalizedText("Finding.DiskLow.Title", "C:"),
        new LocalizedText("Finding.DiskLow.Explanation", "C:", 9),
        "Win32_LogicalDisk DeviceID=C:");

    [Fact]
    public void Finding_Equality_IsByValue()
    {
        Sample().Should().Be(Sample());
    }

    [Fact]
    public void Severity_OrderIsCriticalHighest()
    {
        (Severity.Critical > Severity.Warning).Should().BeTrue();
        (Severity.Warning > Severity.Info).Should().BeTrue();
    }
}
