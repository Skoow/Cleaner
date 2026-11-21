using System.Windows.Controls;
using PCDoc.Presentation.SystemPage;

namespace PCDoc.App.Views;

/// <summary>Page Système (lecture seule). Les données sont lues à la première ouverture.</summary>
public partial class SystemPage : UserControl
{
    public SystemPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is SystemPageViewModel vm)
            {
                await vm.EnsureLoadedAsync().ConfigureAwait(true);
            }
        };
    }
}
