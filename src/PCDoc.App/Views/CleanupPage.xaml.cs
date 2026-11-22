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
