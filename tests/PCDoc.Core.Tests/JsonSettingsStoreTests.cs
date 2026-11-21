using System.Globalization;
using PCDoc.Core.Localization;
using PCDoc.Core.Settings;

namespace PCDoc.Core.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcdoc-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        new JsonSettingsStore(FilePath, CultureInfo.GetCultureInfo("fr-FR")).Load()
            .Should().Be(new AppSettings(AppLanguage.French, ThemeChoice.System));
    }

    [Fact]
    public void Defaults_NonFrenchWindows_IsEnglish()
    {
        AppSettings.DefaultsFor(CultureInfo.GetCultureInfo("de-DE")).Language.Should().Be(AppLanguage.English);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"Language": 42, "Theme": "Purple"}""")]
    [InlineData("null")]
    public void Load_CorruptFile_ReturnsDefaults(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        new JsonSettingsStore(FilePath, CultureInfo.GetCultureInfo("en-US")).Load()
            .Should().Be(new AppSettings(AppLanguage.English, ThemeChoice.System));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new JsonSettingsStore(FilePath, CultureInfo.InvariantCulture);
        var settings = new AppSettings(AppLanguage.English, ThemeChoice.Dark);

        store.Save(settings);

        store.Load().Should().Be(settings);
    }
}
