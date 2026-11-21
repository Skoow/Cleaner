using System.Resources;
using PCDoc.Core.Localization;

namespace PCDoc.Modules.Optimize;

/// <summary>Point d'entrée du module Optimisation.</summary>
public static class OptimizeModule
{
    public const string Id = "optimize";

    public static ResourceManager Resources { get; } = new("PCDoc.Modules.Optimize.Resources.Strings", typeof(OptimizeModule).Assembly);

    public static void RegisterResources(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        localizer.Register(Resources);
    }
}
