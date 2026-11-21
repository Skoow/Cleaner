using System.Windows.Controls;
using PCDoc.Presentation.Diagnostic;

namespace PCDoc.App.Views;

/// <summary>Page Diagnostic : lue à la première ouverture.</summary>
public partial class DiagnosticPage : UserControl
{
    public DiagnosticPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is DiagnosticPageViewModel vm)
            {
                await vm.EnsureLoadedAsync().ConfigureAwait(true);
            }
        };
    }
}
