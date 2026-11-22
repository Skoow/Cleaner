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
    IReadOnlyList<string> Gateways, IReadOnlyList<string> Dns, bool DnsFromDhcp, string? Mac);

/// <summary>Serveurs DNS proposés (connus, sans publicité) ; « auto » = ceux du routeur.</summary>
public sealed record DnsChoice(string Id, string? Primary, string? Secondary)
{
    public static readonly IReadOnlyList<DnsChoice> All =
    [
        new("auto", null, null),
        new("cloudflare", "1.1.1.1", "1.0.0.1"),
        new("quad9", "9.9.9.9", "149.112.112.112"),
        new("google", "8.8.8.8", "8.8.4.4"),
    ];
}

/// <summary>Lecture des cartes réseau et changement de DNS (netsh, réversible : les anciens serveurs sont gardés).</summary>
public sealed partial class NetworkTools(IWmiQuery wmi, IRegistryReader registry, IProcessRunner runner)
{
    private const string Interfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    public IReadOnlyList<NetworkAdapterInfo> Adapters()
    {
        try
        {
            var names = wmi.Query(@"root\cimv2", "SELECT GUID, NetConnectionID FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL")
                .Where(r => r.GetValueOrDefault("GUID") is string)
                .GroupBy(r => (string)r["GUID"]!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().GetValueOrDefault("NetConnectionID") as string, StringComparer.OrdinalIgnoreCase);
            return [.. wmi.Query(@"root\cimv2",
                    "SELECT SettingID, Description, IPAddress, DefaultIPGateway, DNSServerSearchOrder, MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE")
                .Where(r => r.GetValueOrDefault("SettingID") is string)
                .Select(r =>
                {
                    var guid = (string)r["SettingID"]!;
                    var configured = registry.GetValue(RegistryHive.LocalMachine, $@"{Interfaces}\{guid}", "NameServer", RegistryView.Registry64) as string;
                    return new NetworkAdapterInfo(names.GetValueOrDefault(guid) ?? guid, r.GetValueOrDefault("Description") as string ?? string.Empty, guid,
                        r.GetValueOrDefault("IPAddress") as string[] ?? [], r.GetValueOrDefault("DefaultIPGateway") as string[] ?? [],
                        r.GetValueOrDefault("DNSServerSearchOrder") as string[] ?? [], string.IsNullOrWhiteSpace(configured), r.GetValueOrDefault("MACAddress") as string);
                })];
        }
        catch (WmiUnavailableException)
        {
            return [];
        }
    }

    /// <summary>Serveurs DNS fixés à la main pour cette carte (vide = ceux du routeur).</summary>
    public IReadOnlyList<string> StaticDns(string guid) =>
        [.. (registry.GetValue(RegistryHive.LocalMachine, $@"{Interfaces}\{guid}", "NameServer", RegistryView.Registry64) as string ?? string.Empty)
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Where(IsIPv4)];

    public async Task SetDnsAsync(string connection, IReadOnlyList<string> servers, CancellationToken cancellationToken)
    {
        if (!IsConnectionName(connection) || servers.Any(s => !IsIPv4(s)) || servers.Count > 4)
        {
            throw new ArgumentException("Invalid DNS change");
        }

        await Netsh(servers.Count == 0
            ? $"interface ipv4 set dnsservers name=\"{connection}\" source=dhcp"
            : $"interface ipv4 set dnsservers name=\"{connection}\" source=static address={servers[0]} register=primary validate=no", cancellationToken).ConfigureAwait(false);
        for (var i = 1; i < servers.Count; i++)
        {
            await Netsh($"interface ipv4 add dnsservers name=\"{connection}\" address={servers[i]} index={i + 1} validate=no", cancellationToken).ConfigureAwait(false);
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
        var payload = new Saved(data.Connection, data.Guid, [.. network.StaticDns(data.Guid)], Target(data));
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, JsonSerializer.Serialize(payload), "Dns.Undo", [data.Connection]));
    }

    public Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Parse(finding) ?? throw Refused();
        return network.SetDnsAsync(data.Connection, Target(data), cancellationToken);
    }

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var s = Read(entry);
        return Task.FromResult(network.StaticDns(s.Guid).SequenceEqual(s.Applied, StringComparer.Ordinal));
    }

    public Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var s = Read(entry);
        return network.SetDnsAsync(s.Connection, s.Previous, cancellationToken);
    }

    private static List<string> Target(Data data) =>
        DnsChoice.All.First(c => c.Id == data.Choice) is { Primary: { } p, Secondary: { } sec } ? [p, sec] : [];

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
                   && s.Previous.Count <= 4 && s.Previous.All(NetworkTools.IsIPv4) && s.Applied.All(NetworkTools.IsIPv4)
                ? s
                : throw new InvalidOperationException("Invalid undo entry");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid undo entry", ex);
        }
    }

    private sealed record Data(string Connection, string Guid, string Choice);

    private sealed record Saved(string Connection, string Guid, List<string> Previous, List<string> Applied);
}
