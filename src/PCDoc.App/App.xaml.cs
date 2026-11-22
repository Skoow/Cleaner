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
#pragma warning disable CA1001 // l'application WPF libère ses ressources dans OnExit
public partial class App : Application
{
#pragma warning disable CA2213 // libéré dans OnExit (cycle de vie WPF, pas IDisposable)
    private System.Threading.Mutex? _instance;
#pragma warning restore CA2213

    private ServiceProvider? _services;
    private Logger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        var clock = Stopwatch.StartNew();
        // Une seule instance (fenêtre ou entretien automatique) : deux séries de corrections sur le même journal se gêneraient.
        _instance = new System.Threading.Mutex(initiallyOwned: true, @"Global\PCDoc-Instance", out var first);
        if (!first)
        {
            if (!e.Args.Contains("--maintenance", StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show("PCDoc est déjà ouvert (ou un entretien automatique est en cours). / PCDoc is already running.", "PCDoc",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        // Plusieurs analyses lancent des outils de Windows en parallèle : assez de fils dès le départ.
        System.Threading.ThreadPool.GetMinThreads(out var workers, out var io);
        System.Threading.ThreadPool.SetMinThreads(Math.Max(workers, 32), io);
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
        PCDoc.Modules.Apps.AppsModule.RegisterResources(localizer);
        PCDoc.Modules.Updates.UpdatesModule.RegisterResources(localizer);
        PCDoc.Modules.Optimize.OptimizeModule.RegisterResources(localizer);
        PCDoc.Modules.Security.SecurityModule.RegisterResources(localizer);
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
        _services.GetRequiredService<PCDoc.Core.Activity.IActivityLog>().Write("app",
            localizer.Format(new PCDoc.Core.Findings.LocalizedText("Activity.Started", AppInfo.ProductName, AppInfo.Version)));
        if (journalPath is not null)
        {
            // Restes mis en quarantaine il y a plus de 7 jours : supprimés définitivement.
            var quarantine = _services.GetRequiredService<PCDoc.Core.Abstractions.IQuarantine>();
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    quarantine.PurgeOlderThan(TimeSpan.FromDays(7));
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or PCDoc.Infrastructure.Writes.UnsafeFolderException)
                {
                    _logger.Warning(ex, "Quarantine purge failed");
                }
            });
        }
        var theme = _services.GetRequiredService<ThemeApplier>();
        theme.Apply(settings.Theme);
        ThemeApplier.Hook(theme);

        if (e.Args.Contains("--maintenance", StringComparer.OrdinalIgnoreCase))
        {
            RunMaintenance(store, journalPath is not null);
            return;
        }

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
        var pipeline = _services.GetRequiredService<PCDoc.Core.Fixing.FixPipeline>();
        pipeline.SessionCompleted += (_, _) => Dispatcher.BeginInvoke(shell.ForgetOtherPages);
        window.Closing += (_, args) =>
        {
            // Fermer pendant une correction laisserait une installation ou une réparation à moitié faite.
            if (pipeline.IsRunning)
            {
                args.Cancel = true;
                shell.ShowError(localizer["App.BusyClosing"]);
            }
        };
        MainWindow = window;
        window.Show();
        _logger.Information("Window shown in {Ms} ms", clock.ElapsedMilliseconds);
        if (settings.CheckAppUpdates)
        {
            _ = CheckAppUpdateAsync(shell, localizer);
        }
    }

    /// <summary>Vérification discrète de la dernière version de PCDoc (désactivable) ; un échec réseau est ignoré.</summary>
    private async Task CheckAppUpdateAsync(ShellViewModel shell, ILocalizer localizer)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true); // après l'affichage, sans ralentir le démarrage
            if (_services is not { } services)
            {
                return;
            }

            var release = await services.GetRequiredService<PCDoc.Core.AppUpdates.IAppUpdater>().CheckAsync(CancellationToken.None).ConfigureAwait(true);
            services.GetRequiredService<PCDoc.Core.AppUpdates.AppUpdateState>().Latest = release;
            if (release is not null)
            {
                shell.AppUpdateBanner = localizer.Format(new PCDoc.Core.Findings.LocalizedText("AppUpdate.Banner", release.Version.ToString(3)));
                _logger?.Information("PCDoc {Version} is available", release.Version);
            }
        }
#pragma warning disable CA1031 // Vérification facultative : pas d'Internet, GitHub indisponible, fermeture en cours… sans effet sur PCDoc.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger?.Information("Update check skipped: {Message}", ex.Message);
        }
    }

    /// <summary>Installateur de PCDoc vérifié, à lancer une fois l'application fermée.</summary>
    internal static string? PendingInstaller { get; set; }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Information("Exit");
        if (PendingInstaller is { } installer && _services is not null)
        {
            try
            {
                _services.GetRequiredService<PCDoc.Core.AppUpdates.IAppUpdater>().LaunchInstaller(installer);
                _logger?.Information("PCDoc installer started");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
            {
                _logger?.Warning(ex, "The PCDoc installer could not be started");
            }
        }

        _services?.Dispose();
        _logger?.Dispose();
        _instance?.Dispose(); // libère le mutex (aussi libéré par Windows à la fin du processus)
        base.OnExit(e);
    }

    /// <summary>Entretien automatique sans fenêtre, puis fermeture (journal et rapport dans l'Historique).</summary>
    private async void RunMaintenance(ISettingsStore store, bool fixesAvailable)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            if (fixesAvailable)
            {
                _services!.GetRequiredService<PCDoc.Core.Fixing.FixPipeline>().ResolveInterrupted();
                await Composition.Maintenance(_services!).RunAsync(store.Load().Maintenance ?? new MaintenanceOptions(), CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }
#pragma warning disable CA1031 // l'entretien automatique ne doit jamais laisser un processus bloqué
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger?.Error(ex, "Automatic maintenance failed");
        }
        finally
        {
            Shutdown();
        }
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
#pragma warning restore CA1001
