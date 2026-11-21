namespace PCDoc.Infrastructure.IntegrationTests;

[Trait("Category", "Windows")]
public class ProcessRunnerTests
{
    [Fact]
    public async Task RunHidden_ReturnsExitCodeAndOutput()
    {
        var lines = new List<string>();

        var code = await new ProcessRunner().RunHiddenAsync("cmd.exe", "/c echo pcdoc-hidden", lines.Add, CancellationToken.None);

        code.Should().Be(0);
        lines.Should().Contain("pcdoc-hidden");
    }

    [Fact]
    public async Task RunHidden_FlushDns_Succeeds() =>
        (await new ProcessRunner().RunHiddenAsync("ipconfig.exe", "/flushdns", null, CancellationToken.None)).Should().Be(0);

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"..\evil.exe")]
    [InlineData("cmd")]
    public async Task RunHidden_RejectsPaths(string tool)
    {
        var act = () => new ProcessRunner().RunHiddenAsync(tool, "", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
