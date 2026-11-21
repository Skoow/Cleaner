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
using PCDoc.Core.Bilan;
using PCDoc.Core.Modules;
using PCDoc.Modules.System;
using Serilog.Core;

namespace PCDoc.App;

/// <summary>Assemblage des services (injection de dépendances). Rien n'est analysé au démarrage.</summary>
internal static class Composition
{
    public static ServiceProvider Build(Logger logger, ILocalizer localizer, ISettingsStore settings)
    {
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

        services.AddSingleton<IScanner>(sp => new SystemScanner(
            sp.GetRequiredService<IInventorySource>(), sp.GetRequiredService<IBootSource>(), sp.GetRequiredService<IAutorunSource>(),
            sp.GetRequiredService<IClock>(),
            (source, ex) => sp.GetRequiredService<Logger>().Warning(ex, "System scan source {Source} failed", source)));
        services.AddSingleton(sp => new BilanOrchestrator(
            sp.GetServices<IScanner>(), sp.GetRequiredService<IClock>(), BilanOrchestrator.DefaultPerModuleTimeout));

        // Interface.
        services.AddSingleton<ThemeApplier>();
        services.AddSingleton<IThemeApplier>(sp => sp.GetRequiredService<ThemeApplier>());
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAppDataCleaner, AppDataCleaner>();
        services.AddSingleton(sp => new ShellViewModel(page => CreatePage(sp, page)));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public static CultureInfo UiCulture => CultureInfo.CurrentUICulture;

    private static object CreatePage(IServiceProvider services, PageId page) => page switch
    {
        PageId.Bilan => new BilanPageViewModel(
            services.GetRequiredService<BilanOrchestrator>(), services.GetRequiredService<ILocalizer>(),
            services.GetRequiredService<IClipboardService>()),
        PageId.System => new SystemPageViewModel(
            services.GetRequiredService<IInventorySource>(), services.GetRequiredService<IBootSource>(),
            services.GetRequiredService<IAutorunSource>(), services.GetRequiredService<ILocalizer>(),
            services.GetRequiredService<IClipboardService>()),
        _ => new SettingsPageViewModel(
            services.GetRequiredService<ILocalizer>(), services.GetRequiredService<ISettingsStore>(),
            services.GetRequiredService<IThemeApplier>(), services.GetRequiredService<IUserProfileResolver>(),
            services.GetRequiredService<IAppDataCleaner>(), services.GetRequiredService<IClipboardService>(),
            AppPaths.LogsDir, ReadLicense),
    };

    /// <summary>Texte de la licence GPL-3.0 livré à côté de l'exécutable (affiché dans la fenêtre).</summary>
    private static string? ReadLicense()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
