using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.AppUpdates;
using PCDoc.Core.Findings;

namespace PCDoc.Presentation.Settings;

/// <summary>Mises à jour de PCDoc : vérification (désactivable), installation vérifiée, ou lien de la page de téléchargement.</summary>
public sealed partial class SettingsPageViewModel
{
    [ObservableProperty]
    private bool _checkAppUpdates = true;

    [ObservableProperty]
    private string? _appUpdateStatus;

    [ObservableProperty]
    private AppRelease? _availableRelease;

    [ObservableProperty]
    private bool _canInstallAppUpdate;

    [ObservableProperty]
    private bool _appUpdateBusy;

    private IAppUpdater? _updater;

    /// <summary>Service de mise à jour (null = indisponible). À l'affectation, le réglage enregistré est relu.</summary>
    public IAppUpdater? Updater
    {
        get => _updater;
        init
        {
            _updater = value;
            _suspendSave = true;
            CheckAppUpdates = _store.Load().CheckAppUpdates;
            _suspendSave = false;
        }
    }

    /// <summary>Ferme PCDoc pour que l'installateur puisse le remplacer.</summary>
    public Action? ExitApp { get; init; }

    /// <summary>Vrai pendant une correction : PCDoc ne doit pas se fermer, l'installation attend.</summary>
    public Func<bool>? IsBusy { get; init; }

    /// <summary>Version déjà trouvée au démarrage : affichée sans nouvelle requête.</summary>
    public AppUpdateState? State
    {
        get => _state;
        init
        {
            _state = value;
            if (value?.Latest is { } release)
            {
                ShowRelease(release);
            }
        }
    }

    private AppUpdateState? _state;

    /// <summary>Version trouvée au démarrage (affichée sans nouvelle requête).</summary>
    public void ShowRelease(AppRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        AvailableRelease = release;
        var blocked = _updater?.InstallBlockedReason();
        CanInstallAppUpdate = blocked is null && release.InstallerUrl is not null;
        AppUpdateStatus = _localizer.Format(new LocalizedText("AppUpdate.Available", release.Version.ToString(3)))
                          + (blocked is null ? string.Empty : " " + _localizer[blocked]);
    }

    partial void OnCheckAppUpdatesChanged(bool value)
    {
        if (!_suspendSave)
        {
            _store.Save(_store.Load() with { CheckAppUpdates = value });
        }
    }

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        if (_updater is null || AppUpdateBusy)
        {
            return;
        }

        AppUpdateBusy = true;
        AppUpdateStatus = _localizer["AppUpdate.Checking"];
        try
        {
            var release = await _updater.CheckAsync(CancellationToken.None).ConfigureAwait(true);
            if (_state is not null)
            {
                _state.Latest = release;
            }

            if (release is null)
            {
                AvailableRelease = null;
                CanInstallAppUpdate = false;
                AppUpdateStatus = _localizer["AppUpdate.UpToDate"];
            }
            else
            {
                ShowRelease(release);
            }
        }
#pragma warning disable CA1031 // Pas d'Internet, GitHub indisponible… : message affiché.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppUpdateStatus = _localizer.Format(new LocalizedText("AppUpdate.Failed", ex.Message));
        }
        finally
        {
            AppUpdateBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallAppUpdateAsync()
    {
        if (_updater is null || AvailableRelease is not { } release || !CanInstallAppUpdate || AppUpdateBusy)
        {
            return;
        }

        if (IsBusy?.Invoke() == true)
        {
            AppUpdateStatus = _localizer["App.BusyClosing"];
            return;
        }

        AppUpdateBusy = true;
        AppUpdateStatus = _localizer["AppUpdate.Downloading"];
        try
        {
            await _updater.InstallAsync(release, null, CancellationToken.None).ConfigureAwait(true);
            AppUpdateStatus = _localizer["AppUpdate.Installing"];
            ExitApp?.Invoke();
        }
#pragma warning disable CA1031 // Somme ou signature refusée, téléchargement interrompu : rien n'est installé, message affiché.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppUpdateStatus = _localizer.Format(new LocalizedText("AppUpdate.Failed", ex.Message));
        }
        finally
        {
            AppUpdateBusy = false;
        }
    }

    /// <summary>Lien copié plutôt qu'ouvert : un navigateur lancé par PCDoc serait lui aussi administrateur.</summary>
    [RelayCommand]
    private void CopyReleaseLink() => _clipboard?.SetText(AvailableRelease?.PageUrl ?? ReleaseFeed.ReleasesPage);
}
