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
        var dllSearchRestricted = NativeSafety.RestrictDllSearch();
        base.OnStartup(e);

        _logger = Logging.CreateFileLogger(AppPaths.LogsDir);
        if (!dllSearchRestricted)
        {
            _logger.Warning("SetDefaultDllDirectories failed: DLL search order not restricted");
        }

        _logger.Information("{Product} {Version} starting", AppInfo.ProductName, AppInfo.Version);

        var store = new JsonSettingsStore(AppPaths.SettingsFile, Composition.UiCulture);
        var settings = store.Load();
        var localizer = new Localizer(key => _logger.Warning("Missing translation key {Key}", key));
        localizer.Register(CoreResources.Manager);
        SystemModule.RegisterResources(localizer);
        PCDoc.Modules.Diagnostic.DiagnosticModule.RegisterResources(localizer);
        PCDoc.Modules.Cleanup.CleanupModule.RegisterResources(localizer);
        localizer.Language = settings.Language;
        Loc.Instance = localizer;

        string? journalPath = null;
        try
        {
            journalPath = System.IO.Path.Combine(PCDoc.Infrastructure.Writes.SecureFolders.Backup, "undo.jsonl");
            PCDoc.Infrastructure.Writes.SecureFolders.SecureFile(journalPath);
        }
        catch (Exception ex) when (ex is PCDoc.Infrastructure.Writes.UnsafeFolderException or System.IO.IOException
                                       or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Error(ex, "Backup folder is not safe: fixes disabled");
            journalPath = null;
        }

        _services = Composition.Build(_logger, localizer, store, journalPath);
        var theme = _services.GetRequiredService<ThemeApplier>();
        theme.Apply(settings.Theme);
        ThemeApplier.Hook(theme);

        var shell = _services.GetRequiredService<ShellViewModel>();
        HookErrors(shell, localizer);

        if (!settings.FirstRunCompleted)
        {
            shell.ShowFirstRun(new FirstRunViewModel(localizer, store, _services.GetRequiredService<IProtectionService>()));
        }

        if (journalPath is null)
        {
            shell.ShowError(localizer["Fix.Disabled"]);
        }
        else
        {
            _services.GetRequiredService<PCDoc.Core.Fixing.FixPipeline>().ResolveInterrupted();
            var interrupted = _services.GetRequiredService<PCDoc.Core.Fixing.UndoJournal>().Interrupted();
            foreach (var entry in interrupted)
            {
                _logger.Warning("Interrupted change found at startup: {Kind} {Finding}", entry.Kind, entry.FindingId);
            }

            if (interrupted.Count > 0)
            {
                shell.ShowError(localizer["History.InterruptedBanner"]);
            }
        }

        var window = new MainWindow(shell, store, _services.GetRequiredService<IAppDataCleaner>());
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
