using Microsoft.Win32.TaskScheduler;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Fixing;
using PCDoc.Infrastructure.Writes;

namespace PCDoc.Infrastructure.IntegrationTests;

/// <summary>Écritures réelles, sur le runner GitHub (machine jetable) uniquement. Chaque test remet tout en place.</summary>
[Trait("Category", "Windows")]
public class WritesTests
{
    private readonly UserProfileResolver _profiles = new();

    [Fact]
    public void RegistryWriter_SetAndDelete_InTargetUserHive()
    {
        var writer = new RegistryWriter(_profiles);
        var reader = new RegistryReader(_profiles);
        const string Key = @"Software\PCDoc-IntegrationTest";

        writer.SetBinary(RegistryHive.TargetUser, Key, "v", [3, 0, 0, 0]);
        reader.GetValue(RegistryHive.TargetUser, Key, "v").Should().BeEquivalentTo(new byte[] { 3, 0, 0, 0 });

        writer.DeleteValue(RegistryHive.TargetUser, Key, "v");
        reader.GetValue(RegistryHive.TargetUser, Key, "v").Should().BeNull();
        writer.FullPath(RegistryHive.TargetUser, Key).Should().StartWith(@"HKEY_USERS\S-1-5-");
    }

    [Fact]
    public void ServiceControl_ChangeAndRestore_RemoteRegistry()
    {
        var control = new ServiceControl(new RegistryReader(_profiles));
        var original = control.GetStartMode("RemoteRegistry");
        try
        {
            control.SetStartMode("RemoteRegistry", ServiceStartMode.Manual);
            control.GetStartMode("RemoteRegistry").Should().Be(ServiceStartMode.Manual);
            control.SetStartMode("RemoteRegistry", ServiceStartMode.Disabled);
            control.GetStartMode("RemoteRegistry").Should().Be(ServiceStartMode.Disabled);
        }
        finally
        {
            control.SetStartMode("RemoteRegistry", original);
        }
    }

    [Fact]
    public void TaskControl_DisableAndEnable_TemporaryTask()
    {
        const string Name = "PCDoc-IntegrationTest";
        using var service = new TaskService();
        var definition = service.NewTask();
        definition.Actions.Add(new ExecAction(@"C:\Windows\System32\cmd.exe", "/c exit"));
        service.RootFolder.RegisterTaskDefinition(Name, definition);
        try
        {
            var control = new TaskControl();
            control.SetEnabled(@"\" + Name, false);
            control.IsEnabled(@"\" + Name).Should().BeFalse();
            control.SetEnabled(@"\" + Name, true);
            control.IsEnabled(@"\" + Name).Should().BeTrue();
            control.IsEnabled(@"\PCDoc-Does-Not-Exist").Should().BeNull();
        }
        finally
        {
            service.RootFolder.DeleteTask(Name, false);
        }
    }

    [Fact]
    public void BackupStore_WritesUnicodeRegFile_InProtectedFolder()
    {
        var path = new BackupStore().SaveRegistryBackup("pcdoc-it-test.reg", "Windows Registry Editor Version 5.00\r\n");
        try
        {
            File.ReadAllText(path).Should().StartWith("Windows Registry Editor");
            var rules = new DirectoryInfo(Path.GetDirectoryName(path)!).GetAccessControl().AreAccessRulesProtected;
            rules.Should().BeTrue("le dossier de sauvegarde n'hérite pas des droits des utilisateurs");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task RestorePoint_NeverThrows()
    {
        var outcome = await new RestorePointService(new SystemClock()).EnsureAsync("PCDoc integration test", CancellationToken.None);

        outcome.Should().BeOneOf(RestorePointOutcome.Created, RestorePointOutcome.ReusedRecent,
            RestorePointOutcome.ProtectionDisabled, RestorePointOutcome.Failed);
    }
}
