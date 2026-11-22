using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Diagnostic;

/// <summary>Carte réseau active : nom de connexion (« Wi-Fi », « Ethernet »), adresses, passerelle, DNS.</summary>
public sealed record NetworkAdapterInfo(string Connection, string Description, string Guid, IReadOnlyList<string> Addresses,
    IReadOnlyList<string> Gateways, IReadOnlyList<string> Dns, bool DnsFromDhcp, string? Mac, bool DhcpEnabled = true);

/// <summary>Serveurs DNS proposés (connus, sans publicité), en IPv4 et IPv6 ; « auto » = ceux du routeur.</summary>
public sealed record DnsChoice(string Id, string? Primary, string? Secondary, string? Primary6 = null, string? Secondary6 = null)
{
    public static readonly IReadOnlyList<DnsChoice> All =
    [
        new("auto", null, null),
        new("cloudflare", "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
        new("quad9", "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"),
        new("google", "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
    ];
}

/// <summary>Lecture des cartes réseau et changement de DNS (netsh, réversible : les anciens serveurs sont gardés).</summary>
public sealed partial class NetworkTools(IWmiQuery wmi, IRegistryReader registry, IProcessRunner runner)
{
    private const string Interfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string Interfaces6 = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces";
    private const int MaxServers = 8;

    public IReadOnlyList<NetworkAdapterInfo> Adapters()
    {
        try
        {
            var names = wmi.Query(@"root\cimv2", "SELECT GUID, NetConnectionID FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL")
                .Where(r => r.GetValueOrDefault("GUID") is string)
                .GroupBy(r => (string)r["GUID"]!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().GetValueOrDefault("NetConnectionID") as string, StringComparer.OrdinalIgnoreCase);
            return [.. wmi.Query(@"root\cimv2",
                    "SELECT SettingID, Description, IPAddress, DefaultIPGateway, DNSServerSearchOrder, MACAddress, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE")
                .Where(r => r.GetValueOrDefault("SettingID") is string)
                .Select(r =>
                {
                    var guid = (string)r["SettingID"]!;
                    var configured = registry.GetValue(RegistryHive.LocalMachine, $@"{Interfaces}\{guid}", "NameServer", RegistryView.Registry64) as string;
                    return new NetworkAdapterInfo(names.GetValueOrDefault(guid) ?? guid, r.GetValueOrDefault("Description") as string ?? string.Empty, guid,
                        r.GetValueOrDefault("IPAddress") as string[] ?? [], r.GetValueOrDefault("DefaultIPGateway") as string[] ?? [],
                        r.GetValueOrDefault("DNSServerSearchOrder") as string[] ?? [], string.IsNullOrWhiteSpace(configured), r.GetValueOrDefault("MACAddress") as string,
                        r.GetValueOrDefault("DHCPEnabled") as bool? ?? true);
                })];
        }
        catch (WmiUnavailableException)
        {
            return [];
        }
    }

    /// <summary>Serveurs DNS fixés à la main pour cette carte (vide = ceux du routeur).</summary>
    public IReadOnlyList<string> StaticDns(string guid, bool ipv6 = false) =>
        [.. (registry.GetValue(RegistryHive.LocalMachine, $@"{(ipv6 ? Interfaces6 : Interfaces)}\{guid}", "NameServer", RegistryView.Registry64) as string ?? string.Empty)
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Where(ipv6 ? IsIPv6 : IsIPv4)];

    /// <summary>Vrai si la carte reçoit son adresse du routeur (DHCP) ; sinon « DNS automatiques » la laisserait sans DNS.</summary>
    public bool DhcpEnabled(string guid) => Configuration(guid)?.GetValueOrDefault("DHCPEnabled") as bool? ?? true;

    /// <summary>
    /// Fixe les DNS d'une carte (liste vide = ceux du routeur). La carte est désignée par son numéro d'interface, retrouvé
    /// depuis son GUID : un renommage de la connexion ne casse pas l'annulation.
    /// </summary>
    public async Task SetDnsAsync(string connection, string guid, bool ipv6, IReadOnlyList<string> servers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(servers);
        if (!IsConnectionName(connection) || !global::System.Guid.TryParse(guid, out _) || servers.Any(s => !(ipv6 ? IsIPv6(s) : IsIPv4(s)))
            || servers.Count > MaxServers)
        {
            throw new ArgumentException("Invalid DNS change");
        }

        var target = Configuration(guid)?.GetValueOrDefault("InterfaceIndex") is { } index
            ? Convert.ToInt32(index, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : connection;
        var family = ipv6 ? "ipv6" : "ipv4";
        await Netsh(servers.Count == 0
            ? $"interface {family} set dnsservers name=\"{target}\" source=dhcp"
            : $"interface {family} set dnsservers name=\"{target}\" source=static address={servers[0]} register=primary validate=no", cancellationToken).ConfigureAwait(false);
        for (var i = 1; i < servers.Count; i++)
        {
            await Netsh($"interface {family} add dnsservers name=\"{target}\" address={servers[i]} index={i + 1} validate=no", cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool IsIPv6(string? text) =>
        text is not null && IPAddress.TryParse(text, out var ip) && ip.AddressFamily == global::System.Net.Sockets.AddressFamily.InterNetworkV6 && !text.Contains('%');

    private IReadOnlyDictionary<string, object?>? Configuration(string guid)
    {
        try
        {
            return wmi.Query(@"root\cimv2", "SELECT SettingID, InterfaceIndex, DHCPEnabled FROM Win32_NetworkAdapterConfiguration")
                .FirstOrDefault(r => string.Equals(r.GetValueOrDefault("SettingID") as string, guid, StringComparison.OrdinalIgnoreCase));
        }
        catch (WmiUnavailableException)
        {
            return null;
        }
    }

    internal static bool IsIPv4(string? text) =>
        text is not null && IPAddress.TryParse(text, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    internal static bool IsConnectionName(string? name) => name is not null && ConnectionName().IsMatch(name);

    private async Task Netsh(string arguments, CancellationToken cancellationToken)
    {
        var exit = await runner.RunHiddenAsync("netsh.exe", arguments, null, cancellationToken, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"netsh {arguments} exited with code {exit}");
        }
    }

    [GeneratedRegex(@"^[\p{L}\p{N} ._()#\-]{1,80}$")]
    private static partial Regex ConnectionName();
}

/// <summary>Change les serveurs DNS d'une connexion ; réversible (anciens serveurs, ou « automatique », remis à l'annulation).</summary>
public sealed class DnsFixer(NetworkTools network) : IFixer
{
    public const string Kind = "diagnostic.dns";

    public string UndoKind => Kind;

    public static Finding FindingFor(NetworkAdapterInfo adapter, DnsChoice choice)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(choice);
        return new Finding($"diagnostic.dns.{adapter.Guid}.{choice.Id}", DiagnosticModule.Id, Severity.Info,
            new LocalizedText("Dns.Title", adapter.Connection, new LocalizedText($"Dns.Choice.{choice.Id}")), new LocalizedText("Dns.Explanation"),
            $"{adapter.Connection}: {choice.Primary ?? "DHCP"} {choice.Secondary}", FixAvailable: true, Risk: RiskLevel.Conditional, Reversible: true,
            FixData: JsonSerializer.Serialize(new Data(adapter.Connection, adapter.Guid, choice.Id)));
    }

    public bool CanFix(Finding finding) => Parse(finding) is not null;

    public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Parse(finding) ?? throw Refused();
        var (v4, v6) = Target(data);
        var payload = new Saved(data.Connection, data.Guid, [.. network.StaticDns(data.Guid)], v4, [.. network.StaticDns(data.Guid, ipv6: true)], v6);
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, JsonSerializer.Serialize(payload), "Dns.Undo", [data.Connection]));
    }

    public async Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Parse(finding) ?? throw Refused();
        var (v4, v6) = Target(data);
        if (v4.Count == 0 && !network.DhcpEnabled(data.Guid))
        {
            throw new InvalidOperationException("This connection has a fixed address: automatic DNS would leave it without DNS");
        }

        await network.SetDnsAsync(data.Connection, data.Guid, ipv6: false, v4, cancellationToken).ConfigureAwait(false);
        await network.SetDnsAsync(data.Connection, data.Guid, ipv6: true, v6, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var s = Read(entry);
        return Task.FromResult(network.StaticDns(s.Guid).SequenceEqual(s.Applied, StringComparer.Ordinal)
                               && network.StaticDns(s.Guid, ipv6: true).SequenceEqual(s.Applied6 ?? [], StringComparer.OrdinalIgnoreCase));
    }

    public async Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var s = Read(entry);
        await network.SetDnsAsync(s.Connection, s.Guid, ipv6: false, s.Previous, cancellationToken).ConfigureAwait(false);
        if (s.Previous6 is not null)
        {
            await network.SetDnsAsync(s.Connection, s.Guid, ipv6: true, s.Previous6, cancellationToken).ConfigureAwait(false);
        }
    }

    private static (List<string> V4, List<string> V6) Target(Data data) =>
        DnsChoice.All.First(c => c.Id == data.Choice) is { Primary: { } p, Secondary: { } sec } c
            ? ([p, sec], c is { Primary6: { } p6, Secondary6: { } s6 } ? [p6, s6] : [])
            : ([], []);

    private static InvalidOperationException Refused() => new("DNS change refused");

    private static Data? Parse(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        try
        {
            return finding.FixData is { Length: < 1024 } json && JsonSerializer.Deserialize<Data>(json) is { } d
                   && NetworkTools.IsConnectionName(d.Connection) && Guid.TryParse(d.Guid, out _) && DnsChoice.All.Any(c => c.Id == d.Choice)
                   && finding.Id == $"diagnostic.dns.{d.Guid}.{d.Choice}" && (int)finding.Risk >= (int)RiskLevel.Conditional
                ? d
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Saved Read(UndoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            return JsonSerializer.Deserialize<Saved>(entry.Payload) is { } s && NetworkTools.IsConnectionName(s.Connection) && Guid.TryParse(s.Guid, out _)
                   && s.Previous.Count <= 8 && s.Previous.All(NetworkTools.IsIPv4) && s.Applied.All(NetworkTools.IsIPv4)
                   && (s.Previous6 is null || (s.Previous6.Count <= 8 && s.Previous6.All(NetworkTools.IsIPv6)))
                   && (s.Applied6 is null || s.Applied6.All(NetworkTools.IsIPv6))
                ? s
                : throw new InvalidOperationException("Invalid undo entry");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid undo entry", ex);
        }
    }

    private sealed record Data(string Connection, string Guid, string Choice);

    /// <summary>Previous6/Applied6 absents des entrées écrites avant la prise en charge d'IPv6.</summary>
    private sealed record Saved(string Connection, string Guid, List<string> Previous, List<string> Applied, List<string>? Previous6 = null,
        List<string>? Applied6 = null);
}
