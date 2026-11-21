using System.Resources;
using PCDoc.Core.Localization;

namespace PCDoc.Modules.Updates;

/// <summary>Point d'entrée du module Mises à jour.</summary>
public static class UpdatesModule
{
    public const string Id = "updates";

    public static ResourceManager Resources { get; } = new("PCDoc.Modules.Updates.Resources.Strings", typeof(UpdatesModule).Assembly);

    public static void RegisterResources(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        localizer.Register(Resources);
    }
}
