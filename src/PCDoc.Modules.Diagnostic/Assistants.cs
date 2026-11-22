using System.Globalization;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.Diagnostic.Repairs;

namespace PCDoc.Modules.Diagnostic;

public enum CheckStatus
{
    Ok,
    Problem,
    Unknown,
}

/// <summary>Une vérification de l'assistant : résultat, explication et réparation conseillée.</summary>
public sealed record AssistantCheck(string Id, CheckStatus Status, LocalizedText Text, RepairKind? Repair = null);

public interface IAssistants
{
    Task<IReadOnlyList<AssistantCheck>> NoInternetAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AssistantCheck>> NoSoundAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Assistants guidés : chaque étape vérifie une cause, du plus simple au plus profond, et propose la réparation adaptée.
/// « Pas d'internet » : carte réseau, adresse IP, passerelle, DNS, accès à Internet. « Pas de son » : services audio, périphériques.
/// </summary>
public sealed class Assistants(IWmiQuery wmi, IProcessRunner runner) : IAssistants
{
    private const string Cimv2 = @"root\cimv2";
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(20);

    public async Task<IReadOnlyList<AssistantCheck>> NoInternetAsync(CancellationToken cancellationToken)
    {
        var checks = new List<AssistantCheck>();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> configs;
        try
        {
            configs = await Task.Run(() => wmi.Query(Cimv2,
                "SELECT Description, IPAddress, DefaultIPGateway, DNSServerSearchOrder, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (WmiUnavailableException)
        {
            return [new AssistantCheck("wmi", CheckStatus.Unknown, new LocalizedText("Assistant.Wmi"))];
        }

        if (configs.Count == 0)
        {
            checks.Add(new AssistantCheck("adapter", CheckStatus.Problem, new LocalizedText("Assistant.Net.NoAdapter")));
            return checks;
        }

        checks.Add(new AssistantCheck("adapter", CheckStatus.Ok, new LocalizedText("Assistant.Net.Adapter",
            string.Join(", ", configs.Select(c => c.GetValueOrDefault("Description") as string).OfType<string>()))));

        var addresses = configs.SelectMany(c => c.GetValueOrDefault("IPAddress") as string[] ?? []).Where(a => a.Contains('.', StringComparison.Ordinal)).ToList();
        if (addresses.Count == 0 || addresses.All(a => a.StartsWith("169.254.", StringComparison.Ordinal)))
        {
            // Adresse automatique privée : le routeur (DHCP) n'a pas répondu.
            checks.Add(new AssistantCheck("ip", CheckStatus.Problem, new LocalizedText("Assistant.Net.NoIp"), RepairKind.RenewIp));
            return checks;
        }

        checks.Add(new AssistantCheck("ip", CheckStatus.Ok, new LocalizedText("Assistant.Net.Ip", string.Join(", ", addresses))));

        var gateway = configs.SelectMany(c => c.GetValueOrDefault("DefaultIPGateway") as string[] ?? []).FirstOrDefault(g => IsIPv4(g));
        if (gateway is null)
        {
            checks.Add(new AssistantCheck("gateway", CheckStatus.Problem, new LocalizedText("Assistant.Net.NoGateway"), RepairKind.RenewIp));
            return checks;
        }

        if (!await PingAsync(gateway, cancellationToken).ConfigureAwait(false))
        {
            checks.Add(new AssistantCheck("gateway", CheckStatus.Problem, new LocalizedText("Assistant.Net.GatewayDown", gateway), RepairKind.RenewIp));
            return checks;
        }

        checks.Add(new AssistantCheck("gateway", CheckStatus.Ok, new LocalizedText("Assistant.Net.Gateway", gateway)));

        // Internet sans DNS : une adresse publique répond (serveur DNS de Cloudflare, sans requête nominative).
        var internet = await PingAsync("1.1.1.1", cancellationToken).ConfigureAwait(false);
        var dns = await ResolvesAsync(cancellationToken).ConfigureAwait(false);
        if (!internet && !dns)
        {
            checks.Add(new AssistantCheck("internet", CheckStatus.Problem, new LocalizedText("Assistant.Net.NoInternet"), RepairKind.NetworkReset));
        }
        else if (!dns)
        {
            checks.Add(new AssistantCheck("dns", CheckStatus.Problem, new LocalizedText("Assistant.Net.NoDns"), RepairKind.FlushDns));
        }
        else
        {
            checks.Add(new AssistantCheck("internet", CheckStatus.Ok, new LocalizedText("Assistant.Net.Ok")));
        }

        return checks;
    }

    public async Task<IReadOnlyList<AssistantCheck>> NoSoundAsync(CancellationToken cancellationToken)
    {
        var checks = new List<AssistantCheck>();
        try
        {
            var services = await Task.Run(() => wmi.Query(Cimv2, "SELECT Name, State FROM Win32_Service WHERE Name = 'Audiosrv' OR Name = 'AudioEndpointBuilder'"),
                cancellationToken).ConfigureAwait(false);
            var stopped = services.Where(s => !"Running".Equals(s.GetValueOrDefault("State") as string, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.GetValueOrDefault("Name") as string).OfType<string>().ToList();
            checks.Add(stopped.Count > 0 || services.Count == 0
                ? new AssistantCheck("service", CheckStatus.Problem, new LocalizedText("Assistant.Sound.ServiceStopped"), RepairKind.RestartAudio)
                : new AssistantCheck("service", CheckStatus.Ok, new LocalizedText("Assistant.Sound.Service")));

            var devices = await Task.Run(() => wmi.Query(Cimv2, "SELECT Name, Status, ConfigManagerErrorCode FROM Win32_SoundDevice"), cancellationToken)
                .ConfigureAwait(false);
            if (devices.Count == 0)
            {
                checks.Add(new AssistantCheck("device", CheckStatus.Problem, new LocalizedText("Assistant.Sound.NoDevice")));
            }
            else if (devices.Where(d => Convert.ToInt32(d.GetValueOrDefault("ConfigManagerErrorCode") ?? 0, CultureInfo.InvariantCulture) != 0)
                         .Select(d => d.GetValueOrDefault("Name") as string).OfType<string>().ToList() is { Count: > 0 } broken)
            {
                checks.Add(new AssistantCheck("device", CheckStatus.Problem, new LocalizedText("Assistant.Sound.DeviceError", string.Join(", ", broken))));
            }
            else
            {
                checks.Add(new AssistantCheck("device", CheckStatus.Ok, new LocalizedText("Assistant.Sound.Device",
                    string.Join(", ", devices.Select(d => d.GetValueOrDefault("Name") as string).OfType<string>()))));
                checks.Add(new AssistantCheck("volume", CheckStatus.Unknown, new LocalizedText("Assistant.Sound.CheckVolume")));
            }
        }
        catch (WmiUnavailableException)
        {
            checks.Add(new AssistantCheck("wmi", CheckStatus.Unknown, new LocalizedText("Assistant.Wmi")));
        }

        return checks;
    }

    private static bool IsIPv4(string? text) => text is not null && System.Net.IPAddress.TryParse(text, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    private async Task<bool> PingAsync(string address, CancellationToken cancellationToken) =>
        IsIPv4(address) && await runner.RunHiddenAsync("ping.exe", $"-n 2 -w 1500 {address}", null, cancellationToken, PingTimeout).ConfigureAwait(false) == 0;

    private static async Task<bool> ResolvesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(8));
            return (await System.Net.Dns.GetHostAddressesAsync("www.msftconnecttest.com", limit.Token).ConfigureAwait(false)).Length > 0;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
