using PCDoc.Core.Localization;

namespace PCDoc.Presentation.Tests;

public sealed class SearchTests
{
    private static Localizer Make(AppLanguage language)
    {
        var loc = new Localizer { Language = language };
        PCDoc.Modules.Optimize.OptimizeModule.RegisterResources(loc);
        PCDoc.Modules.Updates.UpdatesModule.RegisterResources(loc);
        return loc;
    }

    [Fact]
    public void Find_IgnoresAccentsAndCase_AndRequiresAllWords()
    {
        var index = SearchIndex.Build();
        var loc = Make(AppLanguage.French);

        var results = SearchIndex.Find(index, "DONNEES office", key => loc[key]);

        results.Should().Contain(r => r.Page == PageId.Privacy && r.Title.Contains("Office", StringComparison.Ordinal));
        SearchIndex.Find(index, "x", key => loc[key]).Should().BeEmpty();
        index.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Shell_Search_NavigatesToTheResultPage()
    {
        var loc = Make(AppLanguage.English);
        var shell = new ShellViewModel(_ => new object()) { Translate = key => loc[key] };

        shell.SearchText = "driver";
        var result = shell.SearchResults.First(r => r.Page == PageId.Updates);
        shell.OpenSearchResultCommand.Execute(result);

        shell.CurrentPage.Should().Be(PageId.Updates);
        shell.SearchText.Should().BeEmpty();
    }
}
