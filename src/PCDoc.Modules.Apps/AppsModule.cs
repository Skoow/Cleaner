using System.Resources;
using PCDoc.Core.Localization;

namespace PCDoc.Modules.Apps;

/// <summary>Point d'entrée du module Applications.</summary>
public static class AppsModule
{
    public const string Id = "apps";

    public static ResourceManager Resources { get; } = new("PCDoc.Modules.Apps.Resources.Strings", typeof(AppsModule).Assembly);

    public static void RegisterResources(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        localizer.Register(Resources);
    }
}
