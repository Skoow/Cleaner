using PCDoc.Core.Abstractions;
using PCDoc.Infrastructure.Writes;

namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public sealed class V09OnWindowsTests
{
    [Fact]
    public void NetworkConnections_ListsListeningPortsWithProcesses()
    {
        var list = new NetworkConnections().List();

        list.Should().Contain(c => c.State == "LISTEN");
        list.Should().Contain(c => c.ProcessName != null);
    }

    [Fact]
    public void BrowserExtensions_DoesNotThrow()
    {
        var act = () => new BrowserExtensions(new UserProfileResolver()).List();

        act.Should().NotThrow();
    }

    [Fact]
    public void Maintenance_IsBlockedUnlessInstalledInProgramFiles()
    {
        var scheduler = new MaintenanceScheduler(new ProcessRunner(), new RemovableDrives(), new PCDoc.Infrastructure.Signatures.SignatureVerifier());

        scheduler.BlockedReason().Should().Be("Maintenance.NotInstalled");
        var act = () => scheduler.Schedule(DayOfWeek.Sunday, 12);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DriverBackup_ExportsToTheProtectedFolder()
    {
        var folder = await new DriverBackup(new ProcessRunner(), new SystemClock()).BackupAsync(CancellationToken.None);
        try
        {
            folder.Should().StartWith(SecureFolders.Root);
            Directory.Exists(folder).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
