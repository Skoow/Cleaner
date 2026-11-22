using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.Security.Tests;

public sealed class SecurityToolsTests
{
    private sealed class Writer(FakeRegistryReader registry) : IRegistryValueWriter
    {
        public void SetValue(RegistryHive hive, string path, string name, object? value) => registry.Set(hive, path, name, value, RegistryView.Registry64);
    }

    [Fact]
    public void NetworkActivity_FlagsProgramsFromTemp_AndHidesClosedConnections()
    {
        var rows = NetworkActivity.Analyze(
        [
            new NetConnection("TCP", "0.0.0.0", 445, null, null, "LISTEN", 4, "System", null),
            new NetConnection("TCP", "10.0.0.2", 50000, "203.0.113.7", 443, "ESTABLISHED", 99, "x", @"C:\Users\Lea\AppData\Local\Temp\x.exe"),
            new NetConnection("TCP", "10.0.0.2", 50001, "203.0.113.7", 443, "TIME_WAIT", 0, null, null),
        ]);

        rows.Should().HaveCount(2);
        rows[0].Suspicious.Should().BeTrue();
    }

    [Fact]
    public async Task ExtensionBlock_AddsPolicyValue_AndUndoRemovesIt()
    {
        var registry = new FakeRegistryReader();
        var key = ExtensionBlockFixer.PolicyKeys["Chrome"];
        registry.Set(RegistryHive.LocalMachine, key, "1", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", RegistryView.Registry64);
        var fixer = new ExtensionBlockFixer(registry, new Writer(registry));
        var extension = new BrowserExtension("Chrome", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Coupons", "1.0", null, "x");
        var finding = ExtensionBlockFixer.FindingFor(extension);

        var entry = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);

        registry.GetValue(RegistryHive.LocalMachine, key, "2", RegistryView.Registry64).Should().Be(extension.Id);
        fixer.CanFix(finding).Should().BeFalse("déjà bloquée");
        await fixer.UndoAsync(entry, CancellationToken.None);
        registry.GetValue(RegistryHive.LocalMachine, key, "2", RegistryView.Registry64).Should().BeNull();
        registry.GetValue(RegistryHive.LocalMachine, key, "1", RegistryView.Registry64).Should().NotBeNull("les blocages existants sont gardés");
        ExtensionBlockFixer.CanBlock(extension with { Browser = "Firefox" }).Should().BeFalse();
        ExtensionBlockFixer.CanBlock(extension with { Id = "../evil" }).Should().BeFalse();
    }

    private sealed class Autoruns(string file) : IAutorunSource
    {
        public AutorunScanResult CollectAll() => new(
        [
            new AutorunEntry(AutorunKind.Startup, "Tool", "Run", file, file, true, new SignatureInfo(SignatureStatus.Unsigned, null, false), false),
            new AutorunEntry(AutorunKind.Startup, "OneDrive", "Run", file + "x", file + "x", true, new SignatureInfo(SignatureStatus.Signed, "Microsoft", false), true),
        ], []);
    }

    private sealed class Vt : IVirusTotal
    {
        public List<string> Hashes { get; } = [];

        public bool HasKey => true;

        public void SetKey(string? apiKey)
        {
        }

        public Task<VirusTotalStats?> LookupAsync(string sha256, CancellationToken cancellationToken)
        {
            Hashes.Add(sha256);
            return Task.FromResult<VirusTotalStats?>(new VirusTotalStats(3, 0, 10, 50));
        }
    }

    [Fact]
    public async Task VirusTotal_SendsOnlyHashes_OfNonMicrosoftStartupFiles()
    {
        var file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "hello");
        try
        {
            var vt = new Vt();
            var rows = await new VirusTotalCheck(new Autoruns(file), vt, (_, _) => Task.CompletedTask).RunAsync(null, CancellationToken.None);

            vt.Hashes.Should().Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
            rows.Should().ContainSingle().Which.Stats!.Malicious.Should().Be(3);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
