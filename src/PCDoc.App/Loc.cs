using System.Windows.Data;
using System.Windows.Markup;
using PCDoc.Core.Localization;

namespace PCDoc.App;

/// <summary>Traducteur partagé par tout le XAML.</summary>
public static class Loc
{
    public static ILocalizer Instance { get; set; } = new Localizer();
}

/// <summary>
/// <c>{app:T Nav.Bilan}</c> : texte traduit, mis à jour à chaud quand la langue change.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
