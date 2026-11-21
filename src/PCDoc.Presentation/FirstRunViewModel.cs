using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;

namespace PCDoc.Presentation;

/// <summary>Protection du système (points de restauration), implémentée par l'application.</summary>
public interface IProtectionService
{
    bool IsEnabled();

    bool Enable();
}

/// <summary>Assistant du premier lancement (dans la fenêtre) : langue, mode, protection du système.</summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly ISettingsStore _store;
    private readonly IProtectionService _protection;

    [ObservableProperty]
    private AppLanguage _language;

    [ObservableProperty]
    private AppMode _mode;

    [ObservableProperty]
    private bool _protectionEnabled;

    [ObservableProperty]
    private bool _protectionFailed;

    public FirstRunViewModel(ILocalizer localizer, ISettingsStore store, IProtectionService protection)
    {
        _localizer = localizer;
        _store = store;
        _protection = protection;
        var current = store.Load();
        _language = current.Language;
        _mode = current.Mode;
        _protectionEnabled = protection.IsEnabled();
    }

    public event EventHandler? Finished;

    partial void OnLanguageChanged(AppLanguage value) => _localizer.Language = value;

    [RelayCommand]
    private void EnableProtection()
    {
        ProtectionEnabled = _protection.Enable() && _protection.IsEnabled();
        ProtectionFailed = !ProtectionEnabled;
    }

    [RelayCommand]
    private void Finish()
    {
        _store.Save(_store.Load() with { Language = Language, Mode = Mode, FirstRunCompleted = true });
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
