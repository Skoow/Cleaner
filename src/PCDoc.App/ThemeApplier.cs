using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Settings;
using PCDoc.Presentation;

namespace PCDoc.App;

/// <summary>
/// Thème Fluent (Application.ThemeMode) + couleurs de gravité adaptées au clair, au sombre et au contraste élevé.
/// En mode « Système », le choix clair/sombre est lu dans le profil de la personne devant le PC.
/// </summary>
public sealed class ThemeApplier(IRegistryReader registry) : IThemeApplier
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private ThemeChoice _current = ThemeChoice.System;

    public void Apply(ThemeChoice theme)
    {
        _current = theme;
        var app = Application.Current;
        app.ThemeMode = theme switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        ApplySeverityColors(app);
    }

    /// <summary>À appeler quand Windows change de thème ou de contraste.</summary>
    public void Refresh() => Apply(_current);

    private void ApplySeverityColors(Application app)
    {
        if (SystemParameters.HighContrast)
        {
            Set(app, "Pcdoc.Critical", SystemColors.HighlightTextColor, SystemColors.HighlightColor);
            Set(app, "Pcdoc.Warning", SystemColors.HighlightTextColor, SystemColors.HighlightColor);
            Set(app, "Pcdoc.Ok", SystemColors.HighlightTextColor, SystemColors.HighlightColor);
            return;
        }

        if (IsDark())
        {
            Set(app, "Pcdoc.Critical", "#FF99A4", "#442726");
            Set(app, "Pcdoc.Warning", "#FCE100", "#433519");
            Set(app, "Pcdoc.Ok", "#6CCB5F", "#393D1B");
        }
        else
        {
            Set(app, "Pcdoc.Critical", "#C42B1C", "#FDE7E9");
            Set(app, "Pcdoc.Warning", "#9D5D00", "#FFF4CE");
            Set(app, "Pcdoc.Ok", "#0F7B0F", "#DFF6DD");
        }
    }

    private bool IsDark() => _current switch
    {
        ThemeChoice.Dark => true,
        ThemeChoice.Light => false,
        _ => registry.GetValue(PCDoc.Core.Abstractions.RegistryHive.TargetUser, Personalize, "AppsUseLightTheme") is int light && light == 0,
    };

    private static void Set(Application app, string key, string foreground, string background) =>
        Set(app, key, (Color)ColorConverter.ConvertFromString(foreground), (Color)ColorConverter.ConvertFromString(background));

    private static void Set(Application app, string key, Color foreground, Color background)
    {
        app.Resources[key] = Frozen(foreground);
        app.Resources[key + ".Background"] = Frozen(background);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    internal static void Hook(ThemeApplier applier) =>
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
            {
                Application.Current?.Dispatcher.BeginInvoke(applier.Refresh);
            }
        };
}

/// <summary>Presse-papiers WPF.</summary>
public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Presse-papiers occupé par un autre programme : on ignore plutôt que de planter.
        }
    }

    public void SetSecret(string text)
    {
        try
        {
            // Formats reconnus par Windows : pas d'historique (Win+V), pas d'envoi au presse-papiers cloud, ignoré des outils de suivi.
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new System.IO.MemoryStream(new byte[4]));
            data.SetData("CanIncludeInClipboardHistory", new System.IO.MemoryStream(new byte[4]));
            data.SetData("CanUploadToCloudClipboard", new System.IO.MemoryStream(new byte[4]));
            Clipboard.SetDataObject(data, copy: true);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    if (Clipboard.ContainsText() && Clipboard.GetText() == text)
                    {
                        Clipboard.Clear();
                    }
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Presse-papiers occupé : laissé.
                }
            };
            timer.Start();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Presse-papiers occupé par un autre programme : on ignore plutôt que de planter.
        }
    }
}
