using System.IO;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Infrastructure;
using PCDoc.Infrastructure.Signatures;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Boot;
using PCDoc.Modules.System.Inventory;
using PCDoc.Presentation;
using PCDoc.Presentation.Bilan;
using PCDoc.Presentation.SystemPage;
using PCDoc.Presentation.Settings;
using PCDoc.Presentation.Fixing;
using PCDoc.Presentation.History;
using PCDoc.Core.Fixing;
using PCDoc.Infrastructure.Writes;
using PCDoc.Modules.System.Advisor;
using PCDoc.Modules.System.Fixes;
using PCDoc.Modules.Diagnostic;
using PCDoc.Modules.Diagnostic.Repairs;
using PCDoc.Presentation.Diagnostic;
using PCDoc.Modules.Cleanup;
using PCDoc.Presentation.Cleanup;
using PCDoc.Modules.Apps;
using PCDoc.Presentation.Apps;
using PCDoc.Modules.Updates;
using PCDoc.Presentation.Updates;
using PCDoc.Modules.Optimize;
using PCDoc.Presentation.Optimize;
using PCDoc.Modules.Security;
using PCDoc.Presentation.Security;
using PCDoc.Presentation.Setup;
using PCDoc.Core.Bilan;
using PCDoc.Core.Modules;
using PCDoc.Modules.System;
using Serilog.Core;

namespace PCDoc.App;

/// <summary>Assemblage des services (injection de dépendances). Rien n'est analysé au démarrage.</summary>
internal static class Composition
{
    /// <param name="journalPath">Journal d'annulation dans le dossier protégé ; null = dossier non sûr, corrections désactivées.</param>
    public static ServiceProvider Build(Logger logger, ILocalizer localizer, ISettingsStore settings, string? journalPath)
    {
        _fixesAvailable = journalPath is not null;
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddSingleton(localizer);
        services.AddSingleton(settings);
        services.AddSingleton<IClock, SystemClock>();

        // Accès à Windows (lecture seule en v0.1).
        services.AddSingleton<IUserProfileResolver, UserProfileResolver>();
        services.AddSingleton<IWmiQuery, WmiQuery>();
        services.AddSingleton<IRegistryReader, RegistryReader>();
        services.AddSingleton<IEventLogQuery, EventLogQuery>();
        services.AddSingleton<IFileProbe, FileProbe>();
        services.AddSingleton<ISignatureVerifier, SignatureVerifier>();
        services.AddSingleton<IScheduledTaskReader, ScheduledTaskReader>();
        services.AddSingleton<IServiceReader, ServiceReader>();

        // Module Système (lecture seule).
        services.AddSingleton<IInventorySource>(sp => new InventoryCollector(
            sp.GetRequiredService<IWmiQuery>(), sp.GetRequiredService<IRegistryReader>(), Win11CpuList.Default));
        services.AddSingleton<IBootSource>(sp => new BootPerformanceReader(sp.GetRequiredService<IEventLogQuery>()));
        services.AddSingleton<IAutorunSource>(sp => new AutorunCollector(
            sp.GetRequiredService<IRegistryReader>(), sp.GetRequiredService<IFileProbe>(), sp.GetRequiredService<IUserProfileResolver>(),
            sp.GetRequiredService<ISignatureVerifier>(), sp.GetRequiredService<IScheduledTaskReader>(), sp.GetRequiredService<IServiceReader>()));

        // Corrections (v0.2) : conseiller, correcteurs, points de restauration, journal d'annulation protégé.
        services.AddSingleton<IConditionProbe>(sp => new ConditionProbe(sp.GetRequiredService<IWmiQuery>(), sp.GetRequiredService<IRegistryReader>()));
        services.AddSingleton(sp => new AutorunAdvisor(AdvisorCatalog.Default, sp.GetRequiredService<IConditionProbe>()));
        services.AddSingleton<RegistryWriter>();
        services.AddSingleton<IRegistryWriter>(sp => sp.GetRequiredService<RegistryWriter>());
        services.AddSingleton<IRegistryValueWriter>(sp => sp.GetRequiredService<RegistryWriter>());
        services.AddSingleton<IServiceControl, ServiceControl>();
        services.AddSingleton<ITaskControl, TaskControl>();
        services.AddSingleton<IBackupStore, BackupStore>();
        services.AddSingleton<IRestorePointService, RestorePointService>();
        services.AddSingleton<IProtectionService, ProtectionService>();
        services.AddSingleton<IFixer>(sp => new AutorunToggleFixer(
            sp.GetRequiredService<IRegistryReader>(), sp.GetRequiredService<IRegistryWriter>(), sp.GetRequiredService<IServiceControl>(),
            sp.GetRequiredService<ITaskControl>(), sp.GetRequiredService<IBackupStore>(), () => DateTimeOffset.Now,
            sp.GetRequiredService<AutorunAdvisor>()));
        // Diagnostic (v0.3) : lecture + réparations cachées.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IDiagnosticSource>(sp => new DiagnosticCollector(sp.GetRequiredService<IEventLogQuery>(),
            sp.GetRequiredService<IWmiQuery>(), sp.GetRequiredService<IRegistryReader>(), sp.GetRequiredService<IClock>()));
        services.AddSingleton<IScanner>(sp => new DiagnosticScanner(sp.GetRequiredService<IDiagnosticSource>()));
        services.AddSingleton(sp => new RepairFixer(sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IFixer>(sp => sp.GetRequiredService<RepairFixer>());
        services.AddSingleton(sp => new NetworkTools(sp.GetRequiredService<IWmiQuery>(), sp.GetRequiredService<IRegistryReader>(), sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IFixer>(sp => new DnsFixer(sp.GetRequiredService<NetworkTools>()));
        services.AddSingleton<IWifiProfiles>(sp => new WifiProfiles(sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IAssistants>(sp => new Assistants(sp.GetRequiredService<IWmiQuery>(), sp.GetRequiredService<IProcessRunner>()));

        // Nettoyage (v0.4).
        services.AddSingleton<IFileSystem, CleanupFileSystem>();
        services.AddSingleton<IRecycleBin, RecycleBin>();
        services.AddSingleton<IKnownFolders, KnownFolders>();
        services.AddSingleton<ICleanupGuards, CleanupGuards>();
        services.AddSingleton(sp => new CleanupEngine(CleanupCatalog.Default, sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IRecycleBin>(), sp.GetRequiredService<IKnownFolders>(), sp.GetRequiredService<IUserProfileResolver>(),
            sp.GetRequiredService<IClock>(), sp.GetRequiredService<ICleanupGuards>()));
        services.AddSingleton<ICleanupSource>(sp => sp.GetRequiredService<CleanupEngine>());
        services.AddSingleton<IScanner>(sp => new CleanupScanner(sp.GetRequiredService<ICleanupSource>()));
        services.AddSingleton<IFixer>(sp => new CleanupFixer(sp.GetRequiredService<CleanupEngine>(), sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<ISecureEraser>(sp => new SecureEraser(sp.GetRequiredService<IUserProfileResolver>()));
        services.AddSingleton<IFixer>(sp => new SecureEraseFixer(sp.GetRequiredService<ISecureEraser>(), sp.GetRequiredService<IProcessRunner>()));

        // Applications (v0.5).
        services.AddSingleton<IUninstallLauncher, UninstallLauncher>();
        services.AddSingleton<IQuarantine, Quarantine>();
        services.AddSingleton<IAppDisplayNames, AppDisplayNames>();
        services.AddSingleton(sp => new AppsEngine(
            new Win32AppReader(sp.GetRequiredService<IRegistryReader>()),
            new StoreAppReader(sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<IAppDisplayNames>(), sp.GetRequiredService<IUserProfileResolver>()),
            BloatwareCatalog.Default, sp.GetRequiredService<IUninstallLauncher>(), sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IQuarantine>(), sp.GetRequiredService<IUserProfileResolver>()));
        services.AddSingleton<IAppsSource>(sp => sp.GetRequiredService<AppsEngine>());
        services.AddSingleton<IScanner>(sp => new AppsScanner(sp.GetRequiredService<IAppsSource>()));
        services.AddSingleton<IFixer>(sp => new UninstallFixer(sp.GetRequiredService<AppsEngine>()));
        services.AddSingleton<IFixer>(sp => new LeftoversFixer(sp.GetRequiredService<AppsEngine>(), sp.GetRequiredService<IQuarantine>()));

        // Mises à jour et optimisation (v0.6).
        services.AddSingleton<IWindowsUpdate, WindowsUpdate>();
        services.AddSingleton<IWingetRunner, WingetRunner>();
        services.AddSingleton<IStoreUpdates>(sp => new StoreUpdates(sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IDriverBackup>(sp => new DriverBackup(sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new UpdatesEngine(sp.GetRequiredService<IWindowsUpdate>(), sp.GetRequiredService<IWingetRunner>(), null,
            sp.GetRequiredService<IDriverBackup>())
        {
            DriversBackedUp = folder => sp.GetRequiredService<PCDoc.Core.Activity.IActivityLog>().Write("updates",
                sp.GetRequiredService<ILocalizer>().Format(new PCDoc.Core.Findings.LocalizedText("Updates.DriverBackup.Done", folder))),
        });
        services.AddSingleton<IUpdatesSource>(sp => sp.GetRequiredService<UpdatesEngine>());
        services.AddSingleton<IScanner>(sp => new UpdatesScanner(sp.GetRequiredService<IUpdatesSource>()));
        services.AddSingleton<IFixer>(sp => new UpdateFixer(sp.GetRequiredService<UpdatesEngine>()));
        services.AddSingleton(sp => new OptimizeEngine(TweakCatalog.Default, sp.GetRequiredService<IRegistryReader>(),
            sp.GetRequiredService<IRegistryValueWriter>(), sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<IWmiQuery>()));
        services.AddSingleton<IOptimizeSource>(sp => sp.GetRequiredService<OptimizeEngine>());
        services.AddSingleton<IScanner>(sp => new OptimizeScanner(sp.GetRequiredService<IOptimizeSource>()));
        services.AddSingleton<IFixer>(sp => new TweakFixer(sp.GetRequiredService<OptimizeEngine>(), sp.GetRequiredService<IRegistryWriter>(),
            sp.GetRequiredService<IBackupStore>(), () => sp.GetRequiredService<IClock>().Now));

        // Confidentialité (v0.8) : même moteur que l'optimisation, catalogue privacy.json.
        services.AddKeyedSingleton<OptimizeEngine>(OptimizeModule.PrivacyId, (sp, _) => new OptimizeEngine(OptimizeModule.Privacy,
            sp.GetRequiredService<IRegistryReader>(), sp.GetRequiredService<IRegistryValueWriter>(), sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IWmiQuery>()));
        services.AddSingleton<IScanner>(sp => new OptimizeScanner(sp.GetRequiredKeyedService<OptimizeEngine>(OptimizeModule.PrivacyId), OptimizeModule.PrivacyId));
        services.AddSingleton<IFixer>(sp => new TweakFixer(sp.GetRequiredKeyedService<OptimizeEngine>(OptimizeModule.PrivacyId),
            sp.GetRequiredService<IRegistryWriter>(), sp.GetRequiredService<IBackupStore>(), () => sp.GetRequiredService<IClock>().Now,
            OptimizeModule.PrivacyKind));

        // Sécurité et clés USB (v0.7).
        services.AddSingleton<IDefenderCommand, DefenderCommand>();
        services.AddSingleton<IRemovableDrives, RemovableDrives>();
        services.AddSingleton<PCDoc.Core.AppUpdates.IAppUpdater>(sp => new AppUpdater(sp.GetRequiredService<ISignatureVerifier>()));
        services.AddSingleton<PCDoc.Core.AppUpdates.AppUpdateState>();
        services.AddSingleton<IMaintenanceScheduler>(sp => new MaintenanceScheduler(sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<IRemovableDrives>(), sp.GetRequiredService<ISignatureVerifier>()));
        services.AddSingleton(sp =>
        {
            var settings = new OptimizeEngine(SecurityModule.Settings, sp.GetRequiredService<IRegistryReader>(), sp.GetRequiredService<IRegistryValueWriter>(),
                sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<IWmiQuery>());
            return new SecurityEngine(sp.GetRequiredService<IWmiQuery>(), sp.GetRequiredService<IRegistryReader>(), sp.GetRequiredService<IRegistryValueWriter>(),
                sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<IDefenderCommand>(), sp.GetRequiredService<IFileProbe>(),
                sp.GetRequiredService<IRemovableDrives>(), settings, Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            {
                ScanCompleted = (type, exit) => sp.GetRequiredService<PCDoc.Core.Activity.IActivityLog>().Write("security",
                    sp.GetRequiredService<ILocalizer>().Format(new PCDoc.Core.Findings.LocalizedText("Activity.Scan", sp.GetRequiredService<ILocalizer>()[type switch { 1 => "Security.QuickScan", 2 => "Security.FullScan", _ => "Security.ScanDrive" }], exit))),
            };
        });
        services.AddSingleton<ISecuritySource>(sp => sp.GetRequiredService<SecurityEngine>());
        services.AddSingleton<IScanner>(sp => new SecurityScanner(sp.GetRequiredService<ISecuritySource>()));
        services.AddSingleton<IFixer>(sp => new TweakFixer(sp.GetRequiredService<SecurityEngine>().Settings, sp.GetRequiredService<IRegistryWriter>(),
            sp.GetRequiredService<IBackupStore>(), () => sp.GetRequiredService<IClock>().Now, SecurityModule.SettingKind));
        services.AddSingleton<IFixer>(sp => new DefenderFixer(sp.GetRequiredService<SecurityEngine>()));
        services.AddSingleton<IFixer>(sp => new FirewallFixer(sp.GetRequiredService<SecurityEngine>()));
        services.AddSingleton<IFixer>(sp => new FeatureFixer(sp.GetRequiredService<SecurityEngine>()));
        services.AddSingleton<IFixer>(sp => new RegistryRepairFixer(sp.GetRequiredService<SecurityEngine>()));
        services.AddSingleton<IFixer>(sp => new UsbFixer(sp.GetRequiredService<SecurityEngine>()));

        // Outils de sécurité (v0.9) : activité réseau, extensions, seconde analyse, VirusTotal.
        services.AddSingleton<INetworkConnections, NetworkConnections>();
        services.AddSingleton<IBrowserExtensions, BrowserExtensions>();
        services.AddSingleton(sp => new ExtensionBlockFixer(sp.GetRequiredService<IRegistryReader>(), sp.GetRequiredService<IRegistryValueWriter>()));
        services.AddSingleton<IFixer>(sp => sp.GetRequiredService<ExtensionBlockFixer>());
        services.AddSingleton(sp => new ScanFixer(sp.GetRequiredService<SecurityEngine>(), sp.GetRequiredService<ISecondOpinion>()));
        services.AddSingleton<IFixer>(sp => sp.GetRequiredService<ScanFixer>());
        services.AddSingleton<ISecondOpinion>(sp => new SecondOpinion(sp.GetRequiredService<ISignatureVerifier>(),
            sp.GetRequiredService<PCDoc.Core.Activity.IReportWriter>()));
        services.AddSingleton<IVirusTotal>(_ => new VirusTotalClient(Path.Combine(AppPaths.DataRoot, "virustotal.key")));

        services.AddSingleton(sp => new UndoJournal(journalPath ?? Path.Combine(Path.GetTempPath(), "pcdoc-disabled.jsonl"), sp.GetRequiredService<IClock>()));
        services.AddSingleton<PCDoc.Core.Activity.IActivityLog>(sp => journalPath is null
            ? new PCDoc.Core.Activity.NullActivityLog()
            : new PCDoc.Core.Activity.TextActivityLog(Path.Combine(Path.GetDirectoryName(journalPath)!, "activity.txt"), sp.GetRequiredService<IClock>()));
        services.AddSingleton<PCDoc.Core.Activity.IReportWriter, ReportWriter>();
        services.AddSingleton(sp => new FixPipeline(
            sp.GetServices<IFixer>(), sp.GetRequiredService<IRestorePointService>(), sp.GetRequiredService<UndoJournal>())
        {
            Activity = sp.GetRequiredService<PCDoc.Core.Activity.IActivityLog>(),
            Describe = text => sp.GetRequiredService<ILocalizer>().Format(text),
        });

        services.AddSingleton<IScanner>(sp => new SystemScanner(
            sp.GetRequiredService<IInventorySource>(), sp.GetRequiredService<IBootSource>(), sp.GetRequiredService<IAutorunSource>(),
            sp.GetRequiredService<IClock>(),
            (source, ex) => sp.GetRequiredService<Logger>().Warning(ex, "System scan source {Source} failed", source))
        {
            Advisor = sp.GetRequiredService<AutorunAdvisor>(),
        });
        services.AddSingleton(sp => new BilanOrchestrator(
            sp.GetServices<IScanner>(), sp.GetRequiredService<IClock>(), BilanOrchestrator.DefaultPerModuleTimeout));

        // Interface.
        services.AddSingleton<ThemeApplier>();
        services.AddSingleton<IThemeApplier>(sp => sp.GetRequiredService<ThemeApplier>());
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAppDataCleaner, AppDataCleaner>();
        services.AddSingleton(sp => new ShellViewModel(page => CreatePage(sp, page)) {
            Translate = key => sp.GetRequiredService<ILocalizer>()[key],
            IsExpertMode = () => sp.GetRequiredService<ISettingsStore>().Load().Mode == AppMode.Expert,
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public static CultureInfo UiCulture => CultureInfo.CurrentUICulture;

    private static object CreatePage(IServiceProvider services, PageId page) => page switch
    {
        PageId.Bilan => new BilanPageViewModel(
            services.GetRequiredService<BilanOrchestrator>(), services.GetRequiredService<ILocalizer>(),
            services.GetRequiredService<IClipboardService>(), NewFixPanel(services), Mode(services))
        {
            Secrets = () =>
            {
                var p = services.GetRequiredService<IUserProfileResolver>().Resolve();
                return [Environment.MachineName, Environment.UserName, Environment.UserDomainName, p.AccountName, p.AccountName.Split('\\')[^1], p.ProfilePath, p.Sid];
            },
        },
        PageId.System => new SystemPageViewModel(
            services.GetRequiredService<IInventorySource>(), services.GetRequiredService<IBootSource>(),
            services.GetRequiredService<IAutorunSource>(), services.GetRequiredService<ILocalizer>(),
            services.GetRequiredService<IClipboardService>(), services.GetRequiredService<AutorunAdvisor>(),
            NewFixPanel(services), Mode(services))
        {
            Licenses = new LicensesViewModel(new PCDoc.Modules.System.Licenses.LicenseReader(services.GetRequiredService<IWmiQuery>()),
                services.GetRequiredService<ILocalizer>(), services.GetRequiredService<IClipboardService>()),
        },
        PageId.Diagnostic => DiagnosticPage(services),
        PageId.Cleanup => new CleanupPageViewModel(services.GetRequiredService<ICleanupSource>(), services.GetRequiredService<ILocalizer>(),
            NewFixPanel(services), services.GetRequiredService<IClipboardService>(), Mode(services))
        {
            CanErase = path => services.GetRequiredService<ISecureEraser>().IsAllowed(path),
            LocalDrives = () => [.. DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady)
                .Select(d => d.RootDirectory.FullName)],
        },
        PageId.Apps => new AppsPageViewModel(services.GetRequiredService<IAppsSource>(), services.GetRequiredService<ILocalizer>(),
            NewFixPanel(services), Mode(services)),
        PageId.Updates => new UpdatesPageViewModel(services.GetRequiredService<IUpdatesSource>(), services.GetRequiredService<ILocalizer>(),
            NewFixPanel(services), Mode(services), () => services.GetRequiredService<UpdatesEngine>().RebootRequired)
        {
            BackupDrivers = ct => services.GetRequiredService<UpdatesEngine>().BackupDriversAsync(ct, force: true),
            Activity = services.GetRequiredService<PCDoc.Core.Activity.IActivityLog>(),
            UpdateStore = async ct =>
            {
                await services.GetRequiredService<IStoreUpdates>().TriggerAsync(ct).ConfigureAwait(false);
                services.GetRequiredService<PCDoc.Core.Activity.IActivityLog>().Write("updates", services.GetRequiredService<ILocalizer>()["Updates.Store.Done"]);
            },
        },
        PageId.Optimize => new OptimizePageViewModel(services.GetRequiredService<IOptimizeSource>(), services.GetRequiredService<ILocalizer>(),
            NewFixPanel(services), Mode(services)),
        PageId.Security => SecurityPage(services),
        PageId.Privacy => new OptimizePageViewModel(services.GetRequiredKeyedService<OptimizeEngine>(OptimizeModule.PrivacyId),
            services.GetRequiredService<ILocalizer>(), NewFixPanel(services), Mode(services), "Privacy"),
        PageId.Setup => new SetupPageViewModel(new SetupSources(
                services.GetRequiredKeyedService<OptimizeEngine>(OptimizeModule.PrivacyId), services.GetRequiredService<ISecuritySource>(),
                services.GetRequiredService<IOptimizeSource>(), services.GetRequiredService<IAppsSource>(), services.GetRequiredService<IUpdatesSource>(),
                services.GetRequiredService<ICleanupSource>()),
            services.GetRequiredService<ILocalizer>(), NewFixPanel(services), Mode(services))
        {
            TechProfileFolder = new TechProfileStore(AppPaths.ProfilesDir),
            FreeSpace = () => new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!).AvailableFreeSpace,
            Activity = services.GetRequiredService<PCDoc.Core.Activity.IActivityLog>(),
        },
        PageId.History when _fixesAvailable => new HistoryPageViewModel(services.GetRequiredService<FixPipeline>(), services.GetRequiredService<ILocalizer>(),
            services.GetRequiredService<PCDoc.Core.Activity.IActivityLog>(), services.GetRequiredService<PCDoc.Core.Activity.IReportWriter>(),
            lines => PCDoc.Core.Activity.ActivityReport.Build(lines, PCDoc.Core.AppInfo.ProductName, PCDoc.Core.AppInfo.Version, Environment.MachineName,
                Environment.OSVersion.VersionString, DateTimeOffset.Now)),
        PageId.History => services.GetRequiredService<ILocalizer>()["Fix.Disabled"],
        _ => new SettingsPageViewModel(
            services.GetRequiredService<ILocalizer>(), services.GetRequiredService<ISettingsStore>(),
            services.GetRequiredService<IThemeApplier>(), services.GetRequiredService<IUserProfileResolver>(),
            services.GetRequiredService<IAppDataCleaner>(), services.GetRequiredService<IClipboardService>(),
            AppPaths.LogsDir, ReadLicense)
        {
            Scheduler = services.GetRequiredService<IMaintenanceScheduler>(),
            Updater = services.GetRequiredService<PCDoc.Core.AppUpdates.IAppUpdater>(),
            State = services.GetRequiredService<PCDoc.Core.AppUpdates.AppUpdateState>(),
            IsBusy = () => services.GetRequiredService<PCDoc.Core.Fixing.FixPipeline>().IsRunning,
            ExitApp = () => System.Windows.Application.Current?.MainWindow?.Close(),
            RemovableRoots = () => [.. services.GetRequiredService<IRemovableDrives>().List().Select(d => d.Root)],
        },
    };

    private static DiagnosticPageViewModel DiagnosticPage(IServiceProvider services)
    {
        var fixes = NewFixPanel(services);
        return new DiagnosticPageViewModel(services.GetRequiredService<IDiagnosticSource>(), services.GetRequiredService<ILocalizer>(),
            services.GetRequiredService<RepairFixer>(), fixes, services.GetRequiredService<IAssistants>())
        {
            Network = new NetworkViewModel(services.GetRequiredService<NetworkTools>(), services.GetRequiredService<IWifiProfiles>(),
                services.GetRequiredService<ILocalizer>(), services.GetRequiredService<IClipboardService>(), fixes),
        };
    }

    private static SecurityPageViewModel SecurityPage(IServiceProvider services)
    {
        var fixes = NewFixPanel(services);
        return new SecurityPageViewModel(services.GetRequiredService<ISecuritySource>(), services.GetRequiredService<ILocalizer>(), fixes, Mode(services))
        {
            ScanTools = services.GetRequiredService<ScanFixer>(),
            Tools = new SecurityToolsViewModel(services.GetRequiredService<INetworkConnections>(), services.GetRequiredService<IBrowserExtensions>(),
                services.GetRequiredService<ExtensionBlockFixer>(), services.GetRequiredService<ISecondOpinion>(), services.GetRequiredService<IVirusTotal>(),
                new VirusTotalCheck(services.GetRequiredService<IAutorunSource>(), services.GetRequiredService<IVirusTotal>()),
                services.GetRequiredService<PCDoc.Core.Activity.IActivityLog>(), services.GetRequiredService<ILocalizer>(), fixes),
        };
    }

    /// <summary>Entretien automatique sans fenêtre (tâche planifiée lancée avec « --maintenance »).</summary>
    public static PCDoc.Presentation.Maintenance.MaintenanceRunner Maintenance(IServiceProvider services) => new(
        services.GetRequiredService<ICleanupSource>(), services.GetRequiredService<IUpdatesSource>(), services.GetRequiredService<ISecuritySource>(),
        services.GetRequiredService<FixPipeline>(), services.GetRequiredService<PCDoc.Core.Activity.IActivityLog>(),
        services.GetRequiredService<PCDoc.Core.Activity.IReportWriter>(), services.GetRequiredService<ILocalizer>(),
        lines => PCDoc.Core.Activity.ActivityReport.Build(lines, PCDoc.Core.AppInfo.ProductName, PCDoc.Core.AppInfo.Version, Environment.MachineName,
            Environment.OSVersion.VersionString, DateTimeOffset.Now), services.GetRequiredService<IStoreUpdates>());

    /// <summary>Mode actuel, relu à chaque fois (il peut changer dans les Paramètres).</summary>
    private static Func<AppMode> Mode(IServiceProvider services)
    {
        var store = services.GetRequiredService<ISettingsStore>();
        return () => store.Load().Mode;
    }

    private static bool _fixesAvailable;

    private static FixConfirmationViewModel? NewFixPanel(IServiceProvider services) =>
        _fixesAvailable ? new(services.GetRequiredService<FixPipeline>(), services.GetRequiredService<ILocalizer>(), Mode(services)) : null;

    /// <summary>Texte de la licence GPL-3.0 livré à côté de l'exécutable (affiché dans la fenêtre).</summary>
    private static string? ReadLicense()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
