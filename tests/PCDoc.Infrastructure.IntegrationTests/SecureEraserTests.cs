using PCDoc.Infrastructure;
using PCDoc.Infrastructure.Writes;

namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public sealed class SecureEraserTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pcdoc-erase-").FullName;
    private readonly string _outside = Directory.CreateTempSubdirectory("pcdoc-erase-out-").FullName;
    private readonly SecureEraser _eraser = new(new UserProfileResolver());

    public void Dispose()
    {
        foreach (var d in new[] { _root, _outside })
        {
            if (Directory.Exists(d))
            {
                Directory.Delete(d, true);
            }
        }
    }

    [Fact]
    public void IsAllowed_RefusesSystemFolders_ThroughLinksAndShortNames()
    {
        var users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _eraser.IsAllowed(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Should().BeFalse();
        _eraser.IsAllowed(Path.Combine(users, "Public")).Should().BeFalse();
        _eraser.IsAllowed(programData).Should().BeFalse();
        _eraser.IsAllowed(Path.Combine(programData, "Microsoft")).Should().BeFalse();
        _eraser.IsAllowed(@"C:\PROGRA~1").Should().BeFalse();
        _eraser.IsAllowed(AppContext.BaseDirectory).Should().BeFalse();
        if (Directory.Exists(Path.Combine(users, "All Users", "Microsoft")))
        {
            _eraser.IsAllowed(Path.Combine(users, "All Users", "Microsoft")).Should().BeFalse("« All Users » mène à ProgramData");
        }

        _eraser.IsAllowed(_root + @"::$INDEX_ALLOCATION").Should().BeFalse();
        _eraser.IsAllowed(_root).Should().BeTrue();
    }

    [Fact]
    public void Wipe_ErasesFileAndStreams_ReportsLinksAsNotErased()
    {
        var file = Path.Combine(_root, "secret.txt");
        File.WriteAllText(file, "secret");
        File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]");
        File.WriteAllText(Path.Combine(_outside, "precious.txt"), "keep me");
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), _outside);

        var items = _eraser.Expand(_root);
        items.Should().Contain(file).And.Contain(Path.Combine(_root, "link"));
        items.Select(_eraser.Wipe).Should().Equal(items.Select(i => i == file));

        File.Exists(file).Should().BeFalse();
        File.Exists(Path.Combine(_outside, "precious.txt")).Should().BeTrue("la cible d'un lien n'est jamais touchée");
    }
}
