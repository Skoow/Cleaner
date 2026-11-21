using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;

namespace PCDoc.Presentation.Settings;

/// <summary>Paramètres (langue, thème, compte analysé, journal, effacement des données) et À propos.</summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly ISettingsStore _store;
    private readonly IThemeApplier _themeApplier;
    private readonly IUserProfileResolver _profiles;
    private readonly IAppDataCleaner _cleaner;
    private readonly IClipboardService? _clipboard;
    private readonly Func<string?> _licenseLoader;
    private bool _suspendSave;

    [ObservableProperty]
    private AppLanguage _language;

    [ObservableProperty]
    private ThemeChoice _theme;

    [ObservableProperty]
    private bool _confirmingClear;

    [ObservableProperty]
    private bool _clearDone;

    [ObservableProperty]
    private string? _licenseText;

    [ObservableProperty]
    private bool _confirmingExpert;

    [ObservableProperty]
    private bool _isExpert;

    public SettingsPageViewModel(
        ILocalizer localizer,
        ISettingsStore store,
        IThemeApplier theme,
        IUserProfileResolver profiles,
        IAppDataCleaner cleaner,
        IClipboardService? clipboard,
        string logsPath,
        Func<string?> licenseLoader)
    {
        _localizer = localizer;
        _store = store;
        _themeApplier = theme;
        _profiles = profiles;
        _cleaner = cleaner;
        _clipboard = clipboard;
        _licenseLoader = licenseLoader;
        LogsPath = logsPath;

        var current = store.Load();
        _suspendSave = true;
        _language = current.Language;
        _theme = current.Theme;
        _isExpert = current.Mode == AppMode.Expert;
        _suspendSave = false;
        localizer.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AnalyzedAccount));
    }

    public string LogsPath { get; }

#pragma warning disable CA1822 // Propriétés d'instance : liées par le XAML.
    public string VersionText => $"{AppInfo.ProductName} {AppInfo.Version}";

    public IReadOnlyList<ComponentInfo> Components => ThirdPartyComponents.All;
#pragma warning restore CA1822

    /// <summary>Le compte dont PCDoc lit les réglages et programmes (la personne devant le PC).</summary>
    public string AnalyzedAccount
    {
        get
        {
            var profile = _profiles.Resolve();
            return profile.IsProcessUser
                ? profile.AccountName
                : _localizer.Format(new LocalizedText("Settings.AccountOther", profile.AccountName));
        }
    }

    partial void OnLanguageChanged(AppLanguage value)
    {
        _localizer.Language = value;
        Save();
    }

    partial void OnThemeChanged(ThemeChoice value)
    {
        _themeApplier.Apply(value);
        Save();
    }

    [RelayCommand]
    private void CopyLogsPath() => _clipboard?.SetText(LogsPath);

    /// <summary>Passer en mode expert demande une confirmation dans la page.</summary>
    [RelayCommand]
    private void RequestExpert() => ConfirmingExpert = !IsExpert;

    [RelayCommand]
    private void ConfirmExpert()
    {
        ConfirmingExpert = false;
        IsExpert = true;
        _store.Save(_store.Load() with { Mode = AppMode.Expert });
    }

    [RelayCommand]
    private void CancelExpert() => ConfirmingExpert = false;

    [RelayCommand]
    private void SetNovice()
    {
        ConfirmingExpert = false;
        IsExpert = false;
        _store.Save(_store.Load() with { Mode = AppMode.Novice });
    }

    [RelayCommand]
    private void RequestClear()
    {
        ClearDone = false;
        ConfirmingClear = true;
    }

    [RelayCommand]
    private void CancelClear() => ConfirmingClear = false;

    /// <summary>Efface journaux et paramètres, puis revient aux réglages par défaut sans les réécrire sur le disque.</summary>
    [RelayCommand]
    private void ConfirmClear()
    {
        _cleaner.Clear();
        var defaults = AppSettings.DefaultsFor(CultureInfo.CurrentUICulture);
        _suspendSave = true;
        Language = defaults.Language;
        Theme = defaults.Theme;
        _suspendSave = false;
        _themeApplier.Apply(Theme);
        ConfirmingClear = false;
        ClearDone = true;
    }

    [RelayCommand]
    private void ShowLicense() => LicenseText ??= _licenseLoader() ?? "https://www.gnu.org/licenses/gpl-3.0.html";

    private void Save()
    {
        if (!_suspendSave)
        {
            _store.Save(_store.Load() with { Language = Language, Theme = Theme });
        }
    }
}
