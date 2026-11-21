using System.ComponentModel;
using System.Resources;
using PCDoc.Core.Findings;

namespace PCDoc.Core.Localization;

/// <summary>
/// Traductions. L'indexeur est lié par l'interface (<c>{Binding [Clé]}</c>) : changer
/// <see cref="Language"/> lève <c>PropertyChanged("Item[]")</c> et tout se met à jour à chaud.
/// </summary>
public interface ILocalizer : INotifyPropertyChanged
{
    AppLanguage Language { get; set; }

    string this[string key] { get; }

    string Format(LocalizedText text);

    /// <summary>Ajoute les ressources d'un module (chaque module a ses propres fichiers .resx).</summary>
    void Register(ResourceManager resources);
}
