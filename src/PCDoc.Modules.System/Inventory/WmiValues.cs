using System.Globalization;

namespace PCDoc.Modules.System.Inventory;

/// <summary>Conversions tolérantes des valeurs WMI / registre (types variables selon les machines).</summary>
internal static class WmiValues
{
    public static long? Long(object? value) => value switch
    {
        null => null,
        string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null,
        IConvertible c => TryConvert(c),
        _ => null,
    };

    public static int Int(object? value) => (int)Math.Clamp(Long(value) ?? 0, int.MinValue, int.MaxValue);

    public static string? Str(object? value) => value switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    /// <summary>Tableau de codes de caractères (UInt16[] dans WmiMonitorID), arrêté au premier zéro.</summary>
    public static string? CharCodes(object? value)
    {
        if (value is not Array array)
        {
            return Str(value);
        }

        var chars = new List<char>();
        foreach (var item in array)
        {
            var code = Long(item) ?? 0;
            if (code == 0)
            {
                break;
            }

            chars.Add((char)code);
        }

        var text = new string([.. chars]).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Date CIM « yyyyMMddHHmmss.ffffff+zzz » → date.</summary>
    public static DateOnly? CimDate(object? value) =>
        Str(value) is { Length: >= 8 } s
        && DateOnly.TryParseExact(s[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static long? TryConvert(IConvertible c)
    {
        try
        {
            return c.ToInt64(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is OverflowException or FormatException or InvalidCastException)
        {
            return null;
        }
    }
}
