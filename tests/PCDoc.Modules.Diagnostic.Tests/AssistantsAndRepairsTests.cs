using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.Diagnostic.Repairs;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.Diagnostic.Tests;

public sealed class AssistantsAndRepairsTests
{
    private sealed class Runner(Func<string, string, int>? exit = null) : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            Calls.Add($"{system32Tool} {arguments}");
            return Task.FromResult(exit?.Invoke(system32Tool, arguments) ?? 0);
        }
    }

    private static FakeWmiQuery Network(string[] ip, string[]? gateway) => new FakeWmiQuery().Add(@"root\cimv2", "Win32_NetworkAdapterConfiguration",
        new Dictionary<string, object?> { ["Description"] = "Intel Ethernet", ["IPAddress"] = ip, ["DefaultIPGateway"] = gateway });

    [Fact]
    public async Task NoInternet_PrivateAddress_SuggestsRenewIp()
    {
        var checks = await new Assistants(Network(["169.254.10.2"], null), new Runner()).NoInternetAsync(CancellationToken.None);

        checks.Should().HaveCount(2);
        checks[^1].Should().Match<AssistantCheck>(c => c.Status == CheckStatus.Problem && c.Repair == RepairKind.RenewIp);
    }

    [Fact]
    public async Task NoInternet_RouterDown_StopsThere()
    {
        var runner = new Runner((_, args) => args.EndsWith("192.168.1.1", StringComparison.Ordinal) ? 1 : 0);

        var checks = await new Assistants(Network(["192.168.1.20", "fe80::1"], ["192.168.1.1"]), runner).NoInternetAsync(CancellationToken.None);

        checks[^1].Text.Key.Should().Be("Assistant.Net.GatewayDown");
        runner.Calls.Should().Equal("ping.exe -n 2 -w 1500 192.168.1.1");
    }

    [Fact]
    public async Task NoSound_StoppedService_SuggestsRestart()
    {
        var wmi = new FakeWmiQuery()
            .Add(@"root\cimv2", "Win32_Service", new Dictionary<string, object?> { ["Name"] = "Audiosrv", ["State"] = "Stopped" })
            .Add(@"root\cimv2", "Win32_SoundDevice", new Dictionary<string, object?> { ["Name"] = "Realtek Audio", ["ConfigManagerErrorCode"] = 0u });

        var checks = await new Assistants(wmi, new Runner()).NoSoundAsync(CancellationToken.None);

        checks[0].Repair.Should().Be(RepairKind.RestartAudio);
        checks.Should().Contain(c => c.Id == "volume");
    }

    [Fact]
    public async Task WindowsUpdateReset_RestartsServicesEvenIfAFolderIsLocked()
    {
        var renamed = new List<string>();
        var runner = new Runner();
        var fixer = new RepairFixer(runner, folder =>
        {
            renamed.Add(folder);
            if (folder.Contains("catroot2", StringComparison.Ordinal))
            {
                throw new IOException("in use");
            }
        });
        var finding = RepairFixer.FindingFor(RepairKind.WindowsUpdateReset);
        finding.Risk.Should().Be(RiskLevel.Conditional);

        var act = () => fixer.ApplyAsync(finding, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        renamed.Should().Equal("SoftwareDistribution", @"System32\catroot2");
        runner.Calls[^1].Should().Be("net.exe start wuauserv", "les services sont redémarrés malgré l'échec");
    }

    [Fact]
    public async Task NetworkReset_AsksForReboot_AndIgnoresOptionalStepFailures()
    {
        var reboot = false;
        var fixer = new RepairFixer(new Runner((tool, args) => args.Contains("ipv6", StringComparison.Ordinal) ? 1 : 0));
        fixer.RebootRequired += (_, _) => reboot = true;

        await fixer.ApplyAsync(RepairFixer.FindingFor(RepairKind.NetworkReset), CancellationToken.None);

        reboot.Should().BeTrue();
    }
}
