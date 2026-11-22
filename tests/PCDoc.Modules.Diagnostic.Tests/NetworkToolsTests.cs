using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.Diagnostic.Tests;

public sealed class NetworkToolsTests
{
    private const string Guid = "{6B29FC40-CA47-1067-B31D-00DD010662DA}";

    private sealed class Runner : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            Calls.Add(arguments);
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task Dns_ChangeIsReversible_ToThePreviousStaticServers()
    {
        var registry = new FakeRegistryReader();
        registry.Set(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{Guid}", "NameServer", "192.168.1.1", RegistryView.Registry64);
        var wmi = new FakeWmiQuery()
            .Add(@"root\cimv2", "Win32_NetworkAdapter", new Dictionary<string, object?> { ["GUID"] = Guid, ["NetConnectionID"] = "Wi-Fi" })
            .Add(@"root\cimv2", "Win32_NetworkAdapterConfiguration", new Dictionary<string, object?>
            {
                ["SettingID"] = Guid, ["Description"] = "Intel Wi-Fi 6", ["IPAddress"] = new[] { "192.168.1.20" }, ["DNSServerSearchOrder"] = new[] { "192.168.1.1" },
            });
        var runner = new Runner();
        var tools = new NetworkTools(wmi, registry, runner);
        var adapter = tools.Adapters().Single();
        adapter.Connection.Should().Be("Wi-Fi");
        adapter.DnsFromDhcp.Should().BeFalse();

        var fixer = new DnsFixer(tools);
        var finding = DnsFixer.FindingFor(adapter, DnsChoice.All.Single(c => c.Id == "quad9"));
        finding.Risk.Should().Be(RiskLevel.Conditional);
        var entry = await fixer.PrepareAsync(finding, CancellationToken.None);
        await fixer.ApplyAsync(finding, CancellationToken.None);
        await fixer.UndoAsync(entry, CancellationToken.None);

        runner.Calls.Should().Equal(
            "interface ipv4 set dnsservers name=\"Wi-Fi\" source=static address=9.9.9.9 register=primary validate=no",
            "interface ipv4 add dnsservers name=\"Wi-Fi\" address=149.112.112.112 index=2 validate=no",
            "interface ipv6 set dnsservers name=\"Wi-Fi\" source=static address=2620:fe::fe register=primary validate=no",
            "interface ipv6 add dnsservers name=\"Wi-Fi\" address=2620:fe::9 index=2 validate=no",
            "interface ipv4 set dnsservers name=\"Wi-Fi\" source=static address=192.168.1.1 register=primary validate=no",
            "interface ipv6 set dnsservers name=\"Wi-Fi\" source=dhcp");
        // Adresse fixe : « DNS automatiques » refusé (la carte n'aurait plus de DNS).
        wmi.Replace(@"root\cimv2", "Win32_NetworkAdapterConfiguration", new Dictionary<string, object?> { ["SettingID"] = Guid, ["DHCPEnabled"] = false });
        var auto = DnsFixer.FindingFor(adapter, DnsChoice.All[0]);
        var act = () => fixer.ApplyAsync(auto, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        fixer.CanFix(DnsFixer.FindingFor(adapter with { Connection = "Wi-Fi\" & calc" }, DnsChoice.All[1])).Should().BeFalse();
    }
}
