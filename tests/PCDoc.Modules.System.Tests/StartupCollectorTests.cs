using PCDoc.Core.Abstractions;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.System.Tests;

public class StartupCollectorTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnce = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Wow = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string UserStartup = @"C:\Users\Lea\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup";

    private static IReadOnlyList<StartupItem> Collect(FakeRegistryReader reg, FakeFileProbe? files = null) =>
        new StartupCollector(reg, files ?? new FakeFileProbe(), new FakeProfileResolver()).Collect();

    [Fact]
    public void Startup_ReadsTargetUserHive_NotProcessHive()
    {
        // Le faux registre ne sert que la ruche « TargetUser » : le collecteur ne doit jamais lire ailleurs pour HKCU.
        var reg = new FakeRegistryReader()
            .Set(RegistryHive.TargetUser, Run, "Discord", @"""C:\Users\Lea\AppData\Local\Discord\Update.exe"" --processStart Discord.exe");

        var items = Collect(reg);

        items.Should().ContainSingle(i => i.Name == "Discord" && i.Location.StartsWith("HKCU", StringComparison.Ordinal));
    }

    [Fact]
    public void Startup_UserFolderFromTargetProfile()
    {
        var files = new FakeFileProbe().WithContent($@"{UserStartup}\Spotify.lnk",
            LnkParserTests.BuildLnk(@"C:\Users\Lea\AppData\Roaming\Spotify\Spotify.exe", "--minimized"));

        var item = Collect(new FakeRegistryReader(), files).Single();

        item.Name.Should().Be("Spotify.lnk");
        item.CommandLine.Should().Be(@"""C:\Users\Lea\AppData\Roaming\Spotify\Spotify.exe"" --minimized");
        item.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Startup_ApprovedByte03_IsDisabled()
    {
        var reg = new FakeRegistryReader()
            .Set(RegistryHive.TargetUser, Run, "Steam", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent")
            .Set(RegistryHive.TargetUser, $@"{Approved}\Run", "Steam", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Collect(reg).Single().Enabled.Should().BeFalse();
    }

    [Fact]
    public void Startup_NoApprovedValue_IsEnabled()
    {
        var reg = new FakeRegistryReader().Set(RegistryHive.LocalMachine, Run, "SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe");

        Collect(reg).Single().Enabled.Should().BeTrue();
    }

    [Fact]
    public void Startup_ReadsWow6432Node()
    {
        var reg = new FakeRegistryReader()
            .Set(RegistryHive.LocalMachine, Wow, "OldSync", @"C:\Program Files (x86)\OldSync\sync.exe")
            .Set(RegistryHive.LocalMachine, $@"{Approved}\Run32", "OldSync", new byte[] { 0x03 });

        var item = Collect(reg).Single();

        item.Location.Should().Contain("WOW6432Node");
        item.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Startup_RunOnce_IsEnabled()
    {
        var reg = new FakeRegistryReader().Set(RegistryHive.TargetUser, RunOnce, "Setup", @"C:\setup.exe /finish");

        Collect(reg).Single().Location.Should().EndWith("RunOnce");
    }

    [Fact]
    public void Startup_DesktopIniIgnored()
    {
        var files = new FakeFileProbe().WithFiles($@"{UserStartup}\desktop.ini");

        Collect(new FakeRegistryReader(), files).Should().BeEmpty();
    }
}
