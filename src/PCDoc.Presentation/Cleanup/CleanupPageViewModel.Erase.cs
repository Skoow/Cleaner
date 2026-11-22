using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Findings;
using PCDoc.Modules.Cleanup;

namespace PCDoc.Presentation.Cleanup;

/// <summary>Effacement sécurisé : liste de fichiers et dossiers déposés ou saisis, espace libre d'un disque.</summary>
public sealed partial class CleanupPageViewModel
{
    [ObservableProperty]
    private string _erasePath = string.Empty;

    [ObservableProperty]
    private string? _eraseStatus;

    [ObservableProperty]
    private string? _selectedDrive;

    /// <summary>Vrai si le chemin peut être effacé (null = fonction indisponible).</summary>
    public Func<string, bool>? CanErase { get; init; }

    /// <summary>Disques locaux (« C:\ »…) pour l'effacement de l'espace libre.</summary>
    public Func<IReadOnlyList<string>>? LocalDrives { get; init; }

    public ObservableCollection<string> EraseItems { get; } = [];

    public ObservableCollection<string> Drives { get; } = [];

    /// <summary>Chemins déposés sur la page (glisser-déposer).</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            var p = path.Trim().Trim('"');
            if (p.Length == 0 || EraseItems.Contains(p, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (CanErase?.Invoke(p) != true)
            {
                EraseStatus = _localizer["Erase.Refused"];
                continue;
            }

            EraseItems.Add(p);
            EraseStatus = null;
        }
    }

    [RelayCommand]
    private void AddErasePath()
    {
        AddPaths([ErasePath]);
        ErasePath = string.Empty;
    }

    [RelayCommand]
    private void RemoveErasePath(string? path)
    {
        if (path is not null)
        {
            EraseItems.Remove(path);
        }
    }

    [RelayCommand]
    private void EraseSelected()
    {
        if (EraseItems.Count > 0)
        {
            Fixes?.Request([SecureEraseFixer.ForPaths([.. EraseItems])]);
        }
    }

    [RelayCommand]
    private void WipeFreeSpace()
    {
        if (SelectedDrive is { Length: 3 } drive)
        {
            Fixes?.Request([SecureEraseFixer.ForFreeSpace(drive)]);
        }
    }

    private void RefreshDrives()
    {
        if (Drives.Count > 0)
        {
            return;
        }

        foreach (var d in LocalDrives?.Invoke() ?? [])
        {
            Drives.Add(d);
        }

        SelectedDrive = Drives.FirstOrDefault();
    }
}
