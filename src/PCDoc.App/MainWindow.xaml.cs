using System.Windows;
using System.Windows.Controls;
using PCDoc.Core.Settings;
using PCDoc.Presentation;

namespace PCDoc.App;

/// <summary>La seule fenêtre de PCDoc.</summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly ISettingsStore _settings;
    private readonly IAppDataCleaner _cleaner;
    private bool _syncing;

    public MainWindow(ShellViewModel shell, ISettingsStore settings, IAppDataCleaner cleaner)
    {
        _shell = shell;
        _settings = settings;
        _cleaner = cleaner;
        InitializeComponent();
        Title = PCDoc.Core.AppInfo.ProductName;
        DataContext = shell;
        RestorePlacement();
        SyncSelection();
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
            {
                SyncSelection();
            }
        };
        Closing += (_, _) => SavePlacement();
    }

    /// <summary>Flèche bas : vers les résultats ; Entrée : ouvre le premier ; Échap : efface.</summary>
    private void OnSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Down && SearchList.Items.Count > 0)
        {
            SearchList.SelectedIndex = 0;
            (SearchList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Enter && SearchList.Items.Count > 0)
        {
            OpenSearch(SearchList.Items[0]);
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            _shell.SearchText = string.Empty;
            e.Handled = true;
        }
    }

    private void OnSearchListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && SearchList.SelectedItem is { } item)
        {
            OpenSearch(item);
            e.Handled = true;
        }
    }

    private void OnSearchListClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is { } item)
        {
            OpenSearch(item);
        }
    }

    /// <summary>Ouvre la page du résultat puis place le focus dans le menu (il n'est jamais perdu).</summary>
    private void OpenSearch(object item)
    {
        _shell.OpenSearchResultCommand.Execute(item);
        MainNav.Focus();
    }

    private void OnMainNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || MainNav.SelectedItem is null)
        {
            return;
        }

        _shell.NavigateCommand.Execute(
            MainNav.SelectedItem == SetupItem ? PageId.Setup
            : MainNav.SelectedItem == SystemItem ? PageId.System
            : MainNav.SelectedItem == DiagnosticItem ? PageId.Diagnostic
            : MainNav.SelectedItem == CleanupItem ? PageId.Cleanup
            : MainNav.SelectedItem == AppsItem ? PageId.Apps
            : MainNav.SelectedItem == UpdatesItem ? PageId.Updates
            : MainNav.SelectedItem == OptimizeItem ? PageId.Optimize
            : MainNav.SelectedItem == SecurityItem ? PageId.Security
            : MainNav.SelectedItem == PrivacyItem ? PageId.Privacy
            : MainNav.SelectedItem == HistoryItem ? PageId.History
            : PageId.Bilan);
    }

    private void OnBottomNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && SettingsNav.SelectedItem is not null)
        {
            _shell.NavigateCommand.Execute(PageId.Settings);
        }
    }

    /// <summary>Une seule entrée sélectionnée à la fois entre les deux listes du menu.</summary>
    private void SyncSelection()
    {
        _syncing = true;
        MainNav.SelectedItem = _shell.CurrentPage switch
        {
            PageId.Bilan => BilanItem,
            PageId.Setup => SetupItem,
            PageId.System => SystemItem,
            PageId.Diagnostic => DiagnosticItem,
            PageId.Cleanup => CleanupItem,
            PageId.Apps => AppsItem,
            PageId.Updates => UpdatesItem,
            PageId.Optimize => OptimizeItem,
            PageId.Security => SecurityItem,
            PageId.Privacy => PrivacyItem,
            PageId.History => HistoryItem,
            _ => null,
        };
        SettingsNav.SelectedItem = _shell.CurrentPage == PageId.Settings ? SettingsItem : null;
        _syncing = false;
    }

    private void RestorePlacement()
    {
        if (_settings.Load().Window is not { } w || w.Width < MinWidth || w.Height < MinHeight
            || w.Width > SystemParameters.VirtualScreenWidth * 1.5 || w.Height > SystemParameters.VirtualScreenHeight * 1.5)
        {
            return;
        }

        // Fenêtre hors des écrans actuels (écran débranché) : position par défaut.
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(w.Left, w.Top, w.Width, w.Height)))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (w.Left, w.Top, w.Width, w.Height);
        if (w.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        if (_cleaner.HasCleared)
        {
            return; // l'utilisateur vient d'effacer les données de PCDoc : ne rien réécrire
        }

        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var current = _settings.Load();
        _settings.Save(current with
        {
            Window = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized),
        });
    }
}
