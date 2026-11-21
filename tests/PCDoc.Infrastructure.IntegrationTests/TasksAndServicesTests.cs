using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public class TasksAndServicesTests
{
    private static ServiceReader Services() => new(new WmiQuery(), new RegistryReader(new UserProfileResolver()));

    [Fact]
    public void Tasks_ContainsMicrosoftWindowsFolder()
    {
        new ScheduledTaskReader().ReadAll()
            .Should().Contain(t => t.Path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Services_ContainsWinmgmtAutomatic()
    {
        Services().ReadAll().Should().Contain(s =>
            s.Name.Equals("Winmgmt", StringComparison.OrdinalIgnoreCase) && s.StartMode == ServiceStartMode.Automatic && !s.IsDriver);
    }

    [Fact]
    public void Services_DelayedAutostartDetected()
    {
        Services().ReadAll().Should().Contain(s => s.StartMode == ServiceStartMode.AutomaticDelayed);
    }

    [Fact]
    public void Services_IncludeDrivers()
    {
        Services().ReadAll().Should().Contain(s => s.IsDriver);
    }
}
