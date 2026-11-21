using System.Globalization;
using PCDoc.Core.Localization;

namespace PCDoc.Core.Settings;

/// <summary>Paramètres de l'utilisateur.</summary>
public sealed record AppSettings(AppLanguage Language, ThemeChoice Theme)
{
    /// <summary>Français si Windows est en français, sinon anglais ; thème du système.</summary>
    public static AppSettings DefaultsFor(CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);
        var french = uiCulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase);
        return new AppSettings(french ? AppLanguage.French : AppLanguage.English, ThemeChoice.System);
    }
}
