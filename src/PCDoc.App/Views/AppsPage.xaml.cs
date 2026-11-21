using System.Windows.Controls;
using PCDoc.Presentation.Apps;

namespace PCDoc.App.Views;

/// <summary>Page Applications : inventaire lu à la première ouverture.</summary>
public partial class AppsPage : UserControl
{
    public AppsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is AppsPageViewModel vm)
            {
                await vm.EnsureLoadedAsync().ConfigureAwait(true);
            }
        };
    }
}
