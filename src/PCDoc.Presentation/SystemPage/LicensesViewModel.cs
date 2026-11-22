using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Localization;
using PCDoc.Modules.System.Licenses;

namespace PCDoc.Presentation.SystemPage;

public sealed record LicenseRow(string Name, string Description, string Status, string Key);

/// <summary>Onglet Licences : activation de Windows et d'Office, clé d'origine du PC (masquée jusqu'au clic).</summary>
public sealed partial class LicensesViewModel(LicenseReader reader, ILocalizer localizer, IClipboardService? clipboard) : ObservableObject
{
    private string? _oemKey;

    [ObservableProperty]
    private string _oemKeyText = string.Empty;

    [ObservableProperty]
    private bool _hasOemKey;

    public ObservableCollection<LicenseRow> Products { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var report = await Task.Run(reader.Read).ConfigureAwait(true);
        Products.Clear();
        foreach (var p in report.Products)
        {
            Products.Add(new LicenseRow(p.Name, p.Description, localizer[p.IsActivated ? "Lic.Activated" : "Lic.NotActivated"], "•••••-" + p.PartialKey));
        }

        _oemKey = report.OemKey;
        HasOemKey = _oemKey is not null;
        OemKeyText = _oemKey is null ? localizer["Lic.NoOemKey"] : "•••••-•••••-•••••-•••••-" + _oemKey[^5..];
    }

    [RelayCommand]
    private void ShowOemKey()
    {
        if (_oemKey is not null)
        {
            OemKeyText = _oemKey;
        }
    }

    [RelayCommand]
    private void CopyOemKey()
    {
        if (_oemKey is not null)
        {
            clipboard?.SetText(_oemKey);
        }
    }
}
