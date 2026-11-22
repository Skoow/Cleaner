using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Diagnostic;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Diagnostic;

public sealed record AdapterRow(NetworkAdapterInfo Adapter, string Title, string Addresses, string Gateway, string Dns);

public sealed record WifiRow(string Name, string Security, string Key);

public sealed record DnsOption(DnsChoice Choice, string Label);

/// <summary>Onglet Réseau : cartes, DNS au choix (réversible), réseaux Wi-Fi enregistrés et leurs mots de passe à la demande.</summary>
public sealed partial class NetworkViewModel(NetworkTools network, IWifiProfiles wifi, ILocalizer localizer, IClipboardService? clipboard,
    FixConfirmationViewModel? fixes) : ObservableObject
{
    [ObservableProperty]
    private AdapterRow? _selectedAdapter;

    [ObservableProperty]
    private DnsOption? _selectedDns;

    [ObservableProperty]
    private bool _keysShown;

    public ObservableCollection<AdapterRow> Adapters { get; } = [];

    public ObservableCollection<WifiRow> Wifi { get; } = [];

    public IReadOnlyList<DnsOption> DnsOptions => [.. DnsChoice.All.Select(c => new DnsOption(c, localizer[$"Dns.Choice.{c.Id}"]))];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var adapters = await Task.Run(network.Adapters).ConfigureAwait(true);
        Adapters.Clear();
        foreach (var a in adapters)
        {
            Adapters.Add(new AdapterRow(a, $"{a.Connection} — {a.Description}", string.Join(", ", a.Addresses), string.Join(", ", a.Gateways),
                string.Join(", ", a.Dns) + (a.DnsFromDhcp ? " (" + localizer["Dns.Choice.auto"] + ")" : string.Empty)));
        }

        SelectedAdapter = Adapters.FirstOrDefault();
        SelectedDns ??= DnsOptions[0];
        await LoadWifiAsync(withKeys: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ShowWifiKeysAsync() => LoadWifiAsync(withKeys: true);

    [RelayCommand]
    private void CopyWifiKey(WifiRow? row)
    {
        if (row is { Key.Length: > 0 } && KeysShown)
        {
            clipboard?.SetText(row.Key);
        }
    }

    [RelayCommand]
    private void ApplyDns()
    {
        if (SelectedAdapter is { } a && SelectedDns is { } d)
        {
            fixes?.Request([DnsFixer.FindingFor(a.Adapter, d.Choice)]);
        }
    }

    private async Task LoadWifiAsync(bool withKeys)
    {
        var profiles = await Task.Run(() => wifi.ListAsync(withKeys, CancellationToken.None)).ConfigureAwait(true);
        Wifi.Clear();
        foreach (var p in profiles)
        {
            Wifi.Add(new WifiRow(p.Name, p.Authentication, withKeys ? p.Key ?? localizer["Wifi.NoKey"] : "••••••••"));
        }

        KeysShown = withKeys;
    }
}
