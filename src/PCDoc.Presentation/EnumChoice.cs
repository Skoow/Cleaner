namespace PCDoc.Presentation;

/// <summary>
/// Logique des boutons radio liés à une énumération. Le paramètre XAML peut être la valeur elle-même
/// (<c>{x:Static …}</c>) ou son nom (chaîne).
/// </summary>
public static class EnumChoice
{
    public static bool IsSelected(object? value, object? parameter) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    /// <summary>Valeur à écrire quand le bouton est coché ; null quand il est décoché (rien à écrire).</summary>
    public static object? Choose(object? isChecked, object? parameter, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        if (isChecked is not true || parameter is null)
        {
            return null;
        }

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return parameter switch
        {
            string name when enumType.IsEnum => Enum.Parse(enumType, name),
            _ when parameter.GetType() == enumType => parameter,
            _ => null,
        };
    }
}
