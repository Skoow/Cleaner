using System.Resources;
using PCDoc.Core.Localization;

namespace PCDoc.Modules.Optimize;

/// <summary>Point d'entrée du module Optimisation.</summary>
public static class OptimizeModule
{
    public const string Id = "optimize";

    public static ResourceManager Resources { get; } = new("PCDoc.Modules.Optimize.Resources.Strings", typeof(OptimizeModule).Assembly);

    public const string PrivacyId = "privacy";

    public const string PrivacyKind = "privacy.setting";

    /// <summary>Confidentialité (Definitions/privacy.json), même moteur de réglages réversibles.</summary>
    public static TweakCatalog Privacy { get; } = LoadPrivacy();

    private static TweakCatalog LoadPrivacy()
    {
        using var stream = typeof(OptimizeModule).Assembly.GetManifestResourceStream("PCDoc.Definitions.privacy.json")
                           ?? throw new InvalidOperationException("Embedded privacy catalog missing");
        using var reader = new StreamReader(stream);
        return TweakCatalog.Load(reader.ReadToEnd(), PrivacyId, "Privacy");
    }

    public static void RegisterResources(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        localizer.Register(Resources);
    }
}
