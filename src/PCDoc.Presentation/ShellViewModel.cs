using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCDoc.Presentation;

/// <summary>Fenêtre principale : menu latéral, page affichée, bandeau d'erreur (jamais de boîte de dialogue).</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Func<PageId, object> _pageFactory;
    private readonly Dictionary<PageId, object> _pages = [];

    [ObservableProperty]
    private PageId _currentPage;

    [ObservableProperty]
    private object _currentViewModel;

    [ObservableProperty]
    private string? _errorBanner;

    /// <summary>Assistant du premier lancement affiché par-dessus les pages (null = terminé).</summary>
    [ObservableProperty]
    private FirstRunViewModel? _firstRun;

    public void ShowFirstRun(FirstRunViewModel firstRun)
    {
        ArgumentNullException.ThrowIfNull(firstRun);
        firstRun.Finished += (_, _) => FirstRun = null;
        FirstRun = firstRun;
    }

    /// <param name="pageFactory">Crée le ViewModel d'une page à sa première ouverture (démarrage rapide).</param>
    public ShellViewModel(Func<PageId, object> pageFactory)
    {
        _pageFactory = pageFactory;
        _currentViewModel = PageFor(PageId.Bilan);
    }

    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new(PageId.Bilan, "Nav.Bilan", ""),
        new(PageId.System, "Nav.System", ""),
        new(PageId.Diagnostic, "Nav.Diagnostic", "\uE9D9"),
        new(PageId.Cleanup, "Nav.Cleanup", "\uE74D"),
        new(PageId.Apps, "Nav.Apps", "\uE71D"),
        new(PageId.Updates, "Nav.Updates", "\uE895"),
        new(PageId.Optimize, "Nav.Optimize", "\uE945"),
        new(PageId.Security, "Nav.Security", "\uE72E"),
        new(PageId.History, "Nav.History", "\uE81C"),
        new(PageId.Settings, "Nav.Settings", ""),
    ];

    public void ShowError(string message) => ErrorBanner = message;

    /// <summary>Le menu (ListBox) lie directement <see cref="CurrentPage"/> ; la commande sert au clavier et aux liens.</summary>
    [RelayCommand]
    private void Navigate(PageId page) => CurrentPage = page;

    partial void OnCurrentPageChanged(PageId value) => CurrentViewModel = PageFor(value);

    [RelayCommand]
    private void DismissError() => ErrorBanner = null;

    private object PageFor(PageId page)
    {
        if (!_pages.TryGetValue(page, out var vm))
        {
            _pages[page] = vm = _pageFactory(page);
        }

        return vm;
    }
}
