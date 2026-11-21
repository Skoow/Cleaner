using System.Windows.Controls;
using PCDoc.Presentation.History;

namespace PCDoc.App.Views;

/// <summary>Page Historique : relue à chaque ouverture.</summary>
public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as HistoryPageViewModel)?.Refresh();
    }
}
