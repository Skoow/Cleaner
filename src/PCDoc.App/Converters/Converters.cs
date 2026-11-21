using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PCDoc.Core.Findings;

namespace PCDoc.App.Converters;

/// <summary>Gravité → pinceau (« Pcdoc.Critical », « Pcdoc.Warning », « Pcdoc.Ok » du thème courant).</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public string Suffix { get; set; } = string.Empty;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current.TryFindResource((value is Severity s ? s : Severity.Info) switch
        {
            Severity.Critical => "Pcdoc.Critical",
            Severity.Warning => "Pcdoc.Warning",
            _ => "Pcdoc.Ok",
        } + Suffix);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Vrai → Visible, faux → Collapsed (Invert pour l'inverse).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non nul / non vide → Visible.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Égalité avec le paramètre (pour les boutons radio liés à une énumération).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        PCDoc.Presentation.EnumChoice.IsSelected(value, parameter);

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        PCDoc.Presentation.EnumChoice.Choose(value, parameter, targetType) ?? Binding.DoNothing;
}

/// <summary>Rapport 0–1 → hauteur en pixels (paramètre = hauteur maximale).</summary>
public sealed class RatioToLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Math.Max(2, (value is double r ? r : 0) * double.Parse((string)parameter, CultureInfo.InvariantCulture));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Pastille (signature, contrôle Windows 11) → pinceau du thème ; Suffix « .Background » pour le fond.</summary>
public sealed class BadgeToBrushConverter : IValueConverter
{
    public string Suffix { get; set; } = string.Empty;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value?.ToString() switch
        {
            "Signed" or "Pass" => "Pcdoc.Ok",
            "Unsigned" or "Unknown" => "Pcdoc.Warning",
            "Missing" or "Fail" => "Pcdoc.Critical",
            _ => null,
        };
        return key is null ? null : Application.Current.TryFindResource(key + Suffix);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Note 0–100 → arc de cercle (anneau de 120 px, épaisseur 10).</summary>
public sealed class ScoreToArcConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var score = Math.Clamp(value is int s ? s : 0, 0, 100);
        const double Center = 60, Radius = 50;
        if (score >= 100)
        {
            return new System.Windows.Media.EllipseGeometry(new Point(Center, Center), Radius, Radius);
        }

        var angle = score / 100.0 * 2 * Math.PI;
        var start = new Point(Center, Center - Radius);
        var end = new Point(Center + (Radius * Math.Sin(angle)), Center - (Radius * Math.Cos(angle)));
        var figure = new System.Windows.Media.PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new System.Windows.Media.ArcSegment(end, new Size(Radius, Radius), 0, score > 50,
            System.Windows.Media.SweepDirection.Clockwise, true));
        var geometry = new System.Windows.Media.PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Note → couleur (≥ 80 vert, 50–79 orange, &lt; 50 rouge).</summary>
public sealed class ScoreToBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current.TryFindResource((value is int s ? s : 0) switch
        {
            >= 80 => "Pcdoc.Ok",
            >= 50 => "Pcdoc.Warning",
            _ => "Pcdoc.Critical",
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
