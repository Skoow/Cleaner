using System.Resources;

namespace PCDoc.Core.Localization;

/// <summary>Ressources de traduction du Socle (fenêtre, Bilan, Paramètres).</summary>
public static class CoreResources
{
    public static ResourceManager Manager { get; } =
        new("PCDoc.Core.Resources.Strings", typeof(CoreResources).Assembly);
}
