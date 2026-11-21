using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.System.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Quoted_WithArgs() =>
        CommandLineParser.ExtractExecutable("\"C:\\Program Files\\App\\app.exe\" -min", new FakeFileProbe())
            .Should().Be(@"C:\Program Files\App\app.exe");

    [Fact]
    public void Unquoted_WithSpaces_UsesExistingPrefix() =>
        CommandLineParser.ExtractExecutable(@"C:\Program Files\App\app.exe /s",
                new FakeFileProbe().WithFiles(@"C:\Program Files\App\app.exe"))
            .Should().Be(@"C:\Program Files\App\app.exe");

    [Fact]
    public void EnvironmentVariable_Expanded() =>
        CommandLineParser.ExtractExecutable(@"%ProgramFiles%\App\app.exe", new FakeFileProbe().WithFiles(@"C:\Program Files\App\app.exe"))
            .Should().Be(@"C:\Program Files\App\app.exe");

    [Fact]
    public void Rundll32_ReturnsDll() =>
        CommandLineParser.ExtractExecutable(@"rundll32.exe C:\x\y.dll,Entry", new FakeFileProbe())
            .Should().Be(@"C:\x\y.dll");

    [Fact]
    public void Rundll32_QuotedDll_ReturnsDll() =>
        CommandLineParser.ExtractExecutable(@"C:\Windows\System32\rundll32.exe ""C:\a b\y.dll"",Entry arg", new FakeFileProbe())
            .Should().Be(@"C:\a b\y.dll");

    [Fact]
    public void MissingExtension_AddsExe() =>
        CommandLineParser.ExtractExecutable(@"C:\Tools\tool -q", new FakeFileProbe().WithFiles(@"C:\Tools\tool.exe"))
            .Should().Be(@"C:\Tools\tool.exe");

    [Fact]
    public void BareFileName_ResolvedInSystem32() =>
        CommandLineParser.ExtractExecutable("ctfmon.exe", new FakeFileProbe().WithFiles(@"C:\Windows\System32\ctfmon.exe"))
            .Should().Be(@"C:\Windows\System32\ctfmon.exe");

    [Fact]
    public void NothingExists_ReturnsFirstToken() =>
        CommandLineParser.ExtractExecutable(@"C:\Gone\old.exe --run", new FakeFileProbe())
            .Should().Be(@"C:\Gone\old.exe");

    [Fact]
    public void Empty_ReturnsNull() =>
        CommandLineParser.ExtractExecutable("   ", new FakeFileProbe()).Should().BeNull();

    [Theory]
    [InlineData(@"\SystemRoot\System32\drivers\acpi.sys", @"C:\Windows\System32\drivers\acpi.sys")]
    [InlineData(@"System32\drivers\disk.sys", @"C:\Windows\System32\drivers\disk.sys")]
    [InlineData(@"\??\C:\Program Files\Vendor\drv.sys", @"C:\Program Files\Vendor\drv.sys")]
    public void DriverImagePath_Normalized(string imagePath, string expected) =>
        CommandLineParser.NormalizeImagePath(imagePath, new FakeFileProbe()).Should().Be(expected);
}
