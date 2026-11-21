using System.Windows.Controls;
using PCDoc.Presentation.Updates;

namespace PCDoc.App.Views;

/// <summary>Page Mises à jour : recherche lancée à la première ouverture.</summary>
public partial class UpdatesPage : UserControl
{
    public UpdatesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is UpdatesPageViewModel vm)
            {
                await vm.EnsureLoadedAsync().ConfigureAwait(true);
            }
        };
    }
}
