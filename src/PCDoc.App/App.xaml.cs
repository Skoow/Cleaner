using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PCDoc.Core;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Infrastructure;
using PCDoc.Modules.System;
using PCDoc.Presentation;
using Serilog.Core;

namespace PCDoc.App;

/// <summary>Démarrage de PCDoc : protections, journal, paramètres, langue, thème, fenêtre.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private Logger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        var clock = Stopwatch.StartNew();
        NativeSafety.RestrictDllSearchToSystem32();
        base.OnStartup(e);

        _logger = Logging.CreateFileLogger(AppPaths.LogsDir);
        _logger.Information("{Product} {Version} starting", AppInfo.ProductName, AppInfo.Version);

        var store = new JsonSettingsStore(AppPaths.SettingsFile, Composition.UiCulture);
        var settings = store.Load();
        var localizer = new Localizer(key => _logger.Warning("Missing translation key {Key}", key));
        localizer.Register(CoreResources.Manager);
        SystemModule.RegisterResources(localizer);
        localizer.Language = settings.Language;
        Loc.Instance = localizer;

        _services = Composition.Build(_logger, localizer, store);
        var theme = _services.GetRequiredService<ThemeApplier>();
        theme.Apply(settings.Theme);
        ThemeApplier.Hook(theme);

        var shell = _services.GetRequiredService<ShellViewModel>();
        HookErrors(shell, localizer);

        var window = new MainWindow(shell, store);
        MainWindow = window;
        window.Show();
        _logger.Information("Window shown in {Ms} ms", clock.ElapsedMilliseconds);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Information("Exit");
        _services?.Dispose();
        _logger?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Toute erreur inattendue est journalisée et affichée dans un bandeau : jamais de boîte de dialogue.</summary>
    private void HookErrors(ShellViewModel shell, ILocalizer localizer)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.Error(args.Exception, "Unhandled UI exception");
            shell.ShowError(localizer["Error.Unexpected"]);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
            Dispatcher.BeginInvoke(() => shell.ShowError(localizer["Error.Unexpected"]), DispatcherPriority.Background);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.Fatal(args.ExceptionObject as Exception, "Fatal exception");
    }
}
