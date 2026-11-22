using PCDoc.Presentation.Setup;

namespace PCDoc.Presentation.Tests;

public sealed class TechProfileAndMaintenanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcdoc-profiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void Profiles_RoundTrip_AndNamesAreSanitized()
    {
        var store = new TechProfileStore(_dir);

        var path = store.Save(new TechProfile("  Client ../../evil  ", "gaming", ["privacy", "cleanup"], ["VideoLAN.VLC"]));

        Path.GetFileName(path).Should().Be("Client evil.json");
        var loaded = store.List().Should().ContainSingle().Subject;
        loaded.Usage.Should().Be("gaming");
        loaded.Steps.Should().Equal("privacy", "cleanup");
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        store.List().Should().ContainSingle("un fichier abîmé est ignoré");
    }
}
