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

        fs.TryDeleteFile(Path.Combine(_root, "junk.tmp"), _root).Should().BeTrue();
        fs.DeleteEmptySubdirectories(_root);

        Directory.Exists(_root).Should().BeTrue();
        Directory.Exists(Path.Combine(_root, "empty")).Should().BeFalse();
        File.Exists(Path.Combine(_outside, "precious.txt")).Should().BeTrue("la cible d'un lien n'est jamais touchée");
    }

    [Fact]
    public void TryDelete_RefusesPathsThatLeaveTheRoot()
    {
        var precious = Path.Combine(_outside, "precious.txt");
        File.WriteAllText(precious, "keep me");
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), _outside);
        var fs = new CleanupFileSystem();

        fs.TryDeleteFile(Path.Combine(_root, "link", "precious.txt"), _root).Should().BeFalse("un lien au milieu du chemin sort de la racine");
        fs.TryDeleteFile(precious, _root).Should().BeFalse();
        File.Exists(precious).Should().BeTrue();
    }

    [Fact]
    public void TryDelete_RefusesHardLinks_DeletesReadOnly()
    {
        var precious = Path.Combine(_outside, "precious.txt");
        File.WriteAllText(precious, "keep me");
        var hard = Path.Combine(_root, "hard.tmp");
        CreateHardLinkW(hard, precious, IntPtr.Zero).Should().BeTrue();
        var readOnly = Path.Combine(_root, "ro.tmp");
        File.WriteAllText(readOnly, "x");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var fs = new CleanupFileSystem();

        fs.TryDeleteFile(hard, _root).Should().BeFalse("un lien physique partage le contenu avec un autre fichier");
        fs.TryDeleteFile(readOnly, _root).Should().BeTrue();
        File.Exists(precious).Should().BeTrue();
        File.Exists(readOnly).Should().BeFalse();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFile, string existing, IntPtr security);

    [Fact]
    public void RecycleBin_Query_DoesNotThrow()
    {
        var act = () => new RecycleBin().Query();

        act.Should().NotThrow();
    }
}
