using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;
using PCDoc.Infrastructure;
using PCDoc.Infrastructure.Signatures;
using PCDoc.Presentation;
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

        // Interface.
        services.AddSingleton<ThemeApplier>();
        services.AddSingleton<IThemeApplier>(sp => sp.GetRequiredService<ThemeApplier>());
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton(sp => new ShellViewModel(page => CreatePage(sp, page)));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public static CultureInfo UiCulture => CultureInfo.CurrentUICulture;

    private static object CreatePage(IServiceProvider services, PageId page) => page switch
    {
        _ => services.GetRequiredService<ILocalizer>()[$"Nav.{page}"],
    };
}
