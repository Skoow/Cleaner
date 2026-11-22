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
    private bool _oemShown;

    [ObservableProperty]
    private string _oemKeyText = string.Empty;

    [ObservableProperty]
    private bool _hasOemKey;

    /// <summary>« Lecture… », « Licences indisponibles » ou null.</summary>
    [ObservableProperty]
    private string? _status;

    public ObservableCollection<LicenseRow> Products { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Status = localizer["Lic.Reading"];
        var report = await Task.Run(reader.Read).ConfigureAwait(true);
        Status = report.Unavailable ? localizer["Lic.Unavailable"] : report.Products.Count == 0 ? localizer["Lic.None"] : null;
        Products.Clear();
        foreach (var p in report.Products)
        {
            Products.Add(new LicenseRow(p.Name, p.Description, localizer[p.IsActivated ? "Lic.Activated" : "Lic.NotActivated"], "•••••-" + p.PartialKey));
        }

        _oemKey = report.OemKey;
        HasOemKey = _oemKey is not null;
        OemKeyText = _oemKey is null ? localizer["Lic.NoOemKey"] : "•••••-•••••-•••••-•••••-" + (_oemKey.Length > 5 ? _oemKey[^5..] : "•••••");
    }

    [RelayCommand]
    private void ShowOemKey()
    {
        if (_oemKey is not null)
        {
            OemKeyText = _oemKey;
            _oemShown = true;
        }
    }

    [RelayCommand]
    private void CopyOemKey()
    {
        if (_oemKey is not null && _oemShown)
        {
            clipboard?.SetSecret(_oemKey);
        }
    }
}
