using System.Windows.Controls;
using PCDoc.Presentation.Optimize;

namespace PCDoc.App.Views;

/// <summary>Page Optimisation : état lu à la première ouverture.</summary>
public partial class OptimizePage : UserControl
{
    public OptimizePage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is OptimizePageViewModel vm)
            {
                await vm.EnsureLoadedAsync().ConfigureAwait(true);
            }
        };
    }
}
