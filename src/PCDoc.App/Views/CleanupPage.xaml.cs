using System.Windows.Controls;
using PCDoc.Presentation.Cleanup;

namespace PCDoc.App.Views;

/// <summary>Page Nettoyage : analysée à la première ouverture.</summary>
public partial class CleanupPage : UserControl
{
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
