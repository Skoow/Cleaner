using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;

namespace PCDoc.Core.Tests;

public class LocalizerTests
{
    private static Localizer Make(AppLanguage language = AppLanguage.French, List<string>? missing = null)
    {
        var l = new Localizer(missing is null ? null : missing.Add) { Language = language };
        l.Register(CoreResources.Manager);
        return l;
    }

    [Fact]
    public void Indexer_ReturnsFrenchThenEnglishAfterSwitch()
    {
        var sut = Make();
        sut["Nav.Bilan"].Should().Be("Bilan");

        sut.Language = AppLanguage.English;

        sut["Nav.Bilan"].Should().Be("Overview");
    }

    [Fact]
    public void Indexer_UnknownKey_ReturnsMarkedKeyAndReportsIt()
    {
        var missing = new List<string>();
        var sut = Make(missing: missing);

        sut["Nope"].Should().Be("⟦Nope⟧");
        missing.Should().Equal("Nope");
    }

    [Fact]
    public void Switch_RaisesItemIndexerChanged()
    {
        var sut = Make();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)sut).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.Language = AppLanguage.English;

        raised.Should().Contain("Item[]");
    }

    [Fact]
    public void Format_InsertsArgs()
    {
        var sut = Make();

        sut.Format(new LocalizedText("Bilan.ModulesAnalyzed", "Système")).Should().Be("Modules analysés : Système");
    }

    [Fact]
    public void Resources_FrenchAndEnglishHaveSameKeys()
    {
        static HashSet<string> Keys(ResourceManager rm, CultureInfo culture) =>
            rm.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
                .Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();

        var english = Keys(CoreResources.Manager, CultureInfo.InvariantCulture);
        var french = Keys(CoreResources.Manager, CultureInfo.GetCultureInfo("fr"));

        english.Should().NotBeEmpty();
        french.Should().BeEquivalentTo(english);
    }
}
