using System.Collections;
using System.Globalization;
using PCDoc.Core.Localization;
using PCDoc.Presentation;

namespace PCDoc.Presentation.Tests;

public class ShellViewModelTests
{
    private static ShellViewModel Make(List<PageId>? created = null) =>
        new(id =>
        {
            created?.Add(id);
            return $"vm:{id}";
        });

    [Fact]
    public void StartPage_IsBilan()
    {
        var sut = Make();

        sut.CurrentPage.Should().Be(PageId.Bilan);
        sut.CurrentViewModel.Should().Be("vm:Bilan");
    }

    [Fact]
    public void Navigate_ChangesCurrentPageAndViewModel()
    {
        var sut = Make();

        sut.NavigateCommand.Execute(PageId.System);

        sut.CurrentPage.Should().Be(PageId.System);
        sut.CurrentViewModel.Should().Be("vm:System");
    }

    [Fact]
    public void Navigate_CreatesEachPageOnce()
    {
        var created = new List<PageId>();
        var sut = Make(created);

        sut.NavigateCommand.Execute(PageId.System);
        sut.NavigateCommand.Execute(PageId.Bilan);
        sut.NavigateCommand.Execute(PageId.System);

        created.Should().Equal(PageId.Bilan, PageId.System);
    }

    [Fact]
    public void NavItems_AreBilanSystemSettings_InOrder() =>
        Make().NavItems.Select(n => n.Id).Should().Equal(PageId.Bilan, PageId.System, PageId.Diagnostic, PageId.Cleanup, PageId.History, PageId.Settings);

    [Fact]
    public void NavItems_LabelKeysExistInBothLanguages()
    {
        static HashSet<string> Keys(CultureInfo c) => CoreResources.Manager.GetResourceSet(c, true, false)!
            .Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();

        var keys = Make().NavItems.Select(n => n.LabelKey).ToList();

        Keys(CultureInfo.InvariantCulture).Should().Contain(keys);
        Keys(CultureInfo.GetCultureInfo("fr")).Should().Contain(keys);
    }

    [Fact]
    public void ShowError_SetsBannerAndDismissClearsIt()
    {
        var sut = Make();

        sut.ShowError("Boom");
        sut.ErrorBanner.Should().Be("Boom");

        sut.DismissErrorCommand.Execute(null);
        sut.ErrorBanner.Should().BeNull();
    }
}
