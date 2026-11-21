using PCDoc.Core.Localization;
using PCDoc.Core.Settings;

namespace PCDoc.Presentation.Tests;

/// <summary>Bouton radio lié à une énumération (bug C-1 : le paramètre XAML est l'énumération elle-même, pas une chaîne).</summary>
public class EnumChoiceTests
{
    [Fact]
    public void IsSelected_ComparesEnumParameter()
    {
        EnumChoice.IsSelected(AppLanguage.English, AppLanguage.English).Should().BeTrue();
        EnumChoice.IsSelected(AppLanguage.French, AppLanguage.English).Should().BeFalse();
    }

    [Fact]
    public void Choose_WithEnumParameter_ReturnsThatValue() =>
        EnumChoice.Choose(true, ThemeChoice.Dark, typeof(ThemeChoice)).Should().Be(ThemeChoice.Dark);

    [Fact]
    public void Choose_WithStringParameter_ParsesEnum() =>
        EnumChoice.Choose(true, "Light", typeof(ThemeChoice)).Should().Be(ThemeChoice.Light);

    [Fact]
    public void Choose_WhenUnchecked_ReturnsNull() =>
        EnumChoice.Choose(false, ThemeChoice.Dark, typeof(ThemeChoice)).Should().BeNull();
}
