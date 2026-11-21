using System.ComponentModel;
using System.Globalization;
using System.Resources;
using PCDoc.Core.Findings;

namespace PCDoc.Core.Localization;

/// <inheritdoc />
public sealed class Localizer : ILocalizer
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo English = CultureInfo.InvariantCulture; // langue neutre des .resx

    private readonly List<ResourceManager> _managers = [];
    private readonly Action<string>? _onMissingKey;
    private AppLanguage _language = AppLanguage.French;

    /// <param name="onMissingKey">Appelé pour chaque clé introuvable (journalisée par l'application).</param>
    public Localizer(Action<string>? onMissingKey = null) => _onMissingKey = onMissingKey;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value)
            {
                return;
            }

            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    public CultureInfo Culture => _language == AppLanguage.French ? French : English;

    public string this[string key]
    {
        get
        {
            foreach (var manager in _managers)
            {
                var value = manager.GetString(key, Culture);
                if (value is not null)
                {
                    return value;
                }
            }

            _onMissingKey?.Invoke(key);
            return $"⟦{key}⟧";
        }
    }

    public string Format(LocalizedText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var pattern = this[text.Key];
        return text.Args.Count == 0 ? pattern : string.Format(Culture, pattern, [.. text.Args]);
    }

    public void Register(ResourceManager resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!_managers.Contains(resources))
        {
            _managers.Add(resources);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}
