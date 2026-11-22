using System.Globalization;
using PCDoc.Core.Localization;

namespace PCDoc.Core.Settings;

/// <summary>Paramètres de l'utilisateur.</summary>
public sealed record AppSettings(
    AppLanguage Language,
    ThemeChoice Theme,
    WindowPlacement? Window = null,
    AppMode Mode = AppMode.Novice,
    bool FirstRunCompleted = false,
    MaintenanceOptions? Maintenance = null,
    bool CheckAppUpdates = true)
{
    /// <summary>Français si Windows est en français, sinon anglais ; thème du système.</summary>
    public static AppSettings DefaultsFor(CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);
        var french = uiCulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase);
        return new AppSettings(french ? AppLanguage.French : AppLanguage.English, ThemeChoice.System);
    }
}

/// <summary>Entretien automatique (tâche planifiée) : jour, heure et ce qui est fait (uniquement des actions 🟢).</summary>
public sealed record MaintenanceOptions(
    DayOfWeek Day = DayOfWeek.Sunday,
    int Hour = 12,
    bool Cleanup = true,
    bool SoftwareUpdates = true,
    bool WindowsUpdates = true,
    bool DefenderScan = true);

/// <summary>Position et taille de la fenêtre, mémorisées entre deux lancements.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
