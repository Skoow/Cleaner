using System.Resources;
using PCDoc.Core.Localization;

namespace PCDoc.Modules.Cleanup;

/// <summary>Point d'entrée du module Nettoyage.</summary>
public static class CleanupModule
{
    public const string Id = "cleanup";

    public static ResourceManager Resources { get; } = new("PCDoc.Modules.Cleanup.Resources.Strings", typeof(CleanupModule).Assembly);

    public static void RegisterResources(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        localizer.Register(Resources);
    }
}
