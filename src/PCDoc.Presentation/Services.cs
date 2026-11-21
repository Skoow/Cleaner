using PCDoc.Core.Settings;

namespace PCDoc.Presentation;

/// <summary>Applique le thème (implémenté par l'application WPF).</summary>
public interface IThemeApplier
{
    void Apply(ThemeChoice theme);
}

/// <summary>Presse-papiers (implémenté par l'application WPF). Aucune autre fenêtre ne s'ouvre jamais.</summary>
public interface IClipboardService
{
    void SetText(string text);
}
