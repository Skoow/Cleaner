using System.Windows.Controls;
using PCDoc.Presentation.Security;

namespace PCDoc.App.Views;

/// <summary>Page Sécurité : vérifiée à la première ouverture.</summary>
public partial class SecurityPage : UserControl
{
    public SecurityPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is SecurityPageViewModel vm)
            {
                await vm.EnsureLoadedAsync().ConfigureAwait(true);
            }
        };
    }
}
