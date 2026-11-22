using System.Windows.Controls;
using PCDoc.Presentation.Setup;

namespace PCDoc.App.Views;

/// <summary>Page Nouveau PC : analysée à la première ouverture.</summary>
public partial class SetupPage : UserControl
{
    public SetupPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is SetupPageViewModel vm)
            {
                await vm.EnsureLoadedAsync().ConfigureAwait(true);
            }
        };
    }
}
