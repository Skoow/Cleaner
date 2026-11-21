using PCDoc.Core.Abstractions;
using PCDoc.Infrastructure.Signatures;

namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public sealed class SignatureVerifierTests : IDisposable
{
    private static readonly string Windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pcdoc-sig-" + Guid.NewGuid().ToString("N"));
    private readonly SignatureVerifier _sut = new();

    public SignatureVerifierTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Verify_EmbeddedSigned_IsSigned()
    {
        var info = _sut.Verify(Path.Combine(Windir, "explorer.exe"));

        info.Status.Should().Be(SignatureStatus.Signed);
        info.Publisher.Should().Contain("Microsoft");
    }

    [Fact]
    public void Verify_CatalogSignedSystemFile_IsSigned()
    {
        // Les fichiers de System32 sans signature intégrée sont signés par catalogue : on en cherche un.
        var catalogOnly = Directory.EnumerateFiles(Path.Combine(Windir, "System32"), "*.exe")
            .Take(400)
            .Select(path => (path, info: _sut.Verify(path)))
            .FirstOrDefault(x => x.info.IsCatalogSigned);

        catalogOnly.path.Should().NotBeNull("System32 contient toujours des fichiers signés par catalogue");
        catalogOnly.info.Status.Should().Be(SignatureStatus.Signed);
        catalogOnly.info.Publisher.Should().Contain("Microsoft");
    }

    [Fact]
    public void Verify_UnsignedTempFile_IsUnsigned()
    {
        var path = Path.Combine(_tempDir, "not-signed.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 1, 2, 3, 4, 5, 6, 7, 8]);

        _sut.Verify(path).Status.Should().BeOneOf(SignatureStatus.Unsigned, SignatureStatus.Invalid);
    }

    [Fact]
    public void Verify_TamperedSignedFile_IsInvalid()
    {
        var path = Path.Combine(_tempDir, "tampered.exe");
        var bytes = File.ReadAllBytes(Path.Combine(Windir, "explorer.exe"));
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        _sut.Verify(path).Status.Should().Be(SignatureStatus.Invalid);
    }

    [Fact]
    public void Verify_Missing_IsFileNotFound()
    {
        _sut.Verify(Path.Combine(_tempDir, "missing.exe")).Should().Be(SignatureInfo.NotFound);
    }
}
