using System.Resources;
using PCDoc.Core.Localization;

namespace PCDoc.Modules.System;

/// <summary>Point d'entrée du module Système (ressources de traduction).</summary>
public static class SystemModule
{
    public const string Id = "system";

    public static ResourceManager Resources { get; } =
        new("PCDoc.Modules.System.Resources.Strings", typeof(SystemModule).Assembly);

    public static void RegisterResources(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        localizer.Register(Resources);
    }
}
