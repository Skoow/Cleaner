using PCDoc.Infrastructure.Writes;

namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public sealed class CleanupFileSystemTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pcdoc-clean-").FullName;
    private readonly string _outside = Directory.CreateTempSubdirectory("pcdoc-outside-").FullName;

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
    public void Enumerate_DoesNotFollowLinks_DeleteEmptyKeepsRoot()
    {
        File.WriteAllText(Path.Combine(_root, "junk.tmp"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "empty", "deeper"));
        File.WriteAllText(Path.Combine(_outside, "precious.txt"), "keep me");
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), _outside);
        var fs = new CleanupFileSystem();

        var files = fs.EnumerateFiles(_root, recursive: true).Select(f => Path.GetFileName(f.Path)).ToList();
        files.Should().Equal("junk.tmp");

        fs.TryDeleteFile(Path.Combine(_root, "junk.tmp")).Should().BeTrue();
        fs.DeleteEmptySubdirectories(_root);

        Directory.Exists(_root).Should().BeTrue();
        Directory.Exists(Path.Combine(_root, "empty")).Should().BeFalse();
        File.Exists(Path.Combine(_outside, "precious.txt")).Should().BeTrue("la cible d'un lien n'est jamais touchée");
    }

    [Fact]
    public void RecycleBin_Query_DoesNotThrow()
    {
        var act = () => new RecycleBin().Query();

        act.Should().NotThrow();
    }
}
