using System.Windows.Controls;
using PCDoc.Presentation.Cleanup;

namespace PCDoc.App.Views;

/// <summary>Page Nettoyage : analysée à la première ouverture.</summary>
public partial class CleanupPage : UserControl
{
    /// <summary>Fichiers ou dossiers déposés dans l'onglet « Effacement sécurisé ».</summary>
    private void OnEraseDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is CleanupPageViewModel vm && e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            vm.AddPaths(paths);
        }
    }

    /// <summary>
    /// Sélecteurs de fichiers et de dossier : le glisser-déposer depuis l'Explorateur est bloqué par Windows quand
    /// PCDoc tourne en administrateur.
    /// </summary>
    private void OnPickFiles(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, DereferenceLinks = false };
        if (DataContext is CleanupPageViewModel vm && dialog.ShowDialog(System.Windows.Window.GetWindow(this)) == true)
        {
            vm.AddPaths(dialog.FileNames);
        }
    }

    private void OnPickFolder(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true, DereferenceLinks = false };
        if (DataContext is CleanupPageViewModel vm && dialog.ShowDialog(System.Windows.Window.GetWindow(this)) == true)
        {
            vm.AddPaths(dialog.FolderNames);
        }
    }

    public CleanupPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is CleanupPageViewModel vm)
            {
                await vm.EnsureLoadedAsync().ConfigureAwait(true);
            }
        };
    }
}
