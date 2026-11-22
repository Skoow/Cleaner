using System.Resources;
using PCDoc.Core.Localization;

namespace PCDoc.Modules.Security;

/// <summary>Point d'entrée du module Sécurité.</summary>
public static class SecurityModule
{
    public const string Id = "security";

    public static ResourceManager Resources { get; } = new("PCDoc.Modules.Security.Resources.Strings", typeof(SecurityModule).Assembly);

    public const string SettingKind = "security.setting";

    /// <summary>Réglages de sécurité réversibles (Definitions/security.json, même moteur que l'optimisation).</summary>
    public static PCDoc.Modules.Optimize.TweakCatalog Settings { get; } = LoadSettings();

    private static PCDoc.Modules.Optimize.TweakCatalog LoadSettings()
    {
        using var stream = typeof(SecurityModule).Assembly.GetManifestResourceStream("PCDoc.Definitions.security.json")
                           ?? throw new InvalidOperationException("Embedded security catalog missing");
        using var reader = new StreamReader(stream);
        return PCDoc.Modules.Optimize.TweakCatalog.Load(reader.ReadToEnd(), Id, "Security");
    }

    public static void RegisterResources(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        localizer.Register(Resources);
    }
}
