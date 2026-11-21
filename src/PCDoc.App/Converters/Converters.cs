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
        Equals(value?.ToString(), parameter?.ToString());

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string name && targetType.IsEnum ? Enum.Parse(targetType, name) : Binding.DoNothing;
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
