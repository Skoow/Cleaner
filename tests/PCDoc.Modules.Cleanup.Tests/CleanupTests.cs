using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.Cleanup;
using PCDoc.Modules.System.Tests.Fakes;

namespace PCDoc.Modules.Cleanup.Tests;

public class CleanupTests
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private const long MB = 1024 * 1024;
    private const string Local = @"C:\Users\Lea\AppData\Local";

    internal sealed class Clock : IClock
    {
        public DateTimeOffset Now => CleanupTests.Now;
    }

    internal sealed class Profiles : IUserProfileResolver
    {
        public UserProfile Resolve() => new("S-1-5-21-1", @"PC\Lea", @"C:\Users\Lea", @"C:\Users\Lea\AppData\Roaming", Local, false);
    }

    internal sealed class Probe : IFileProbe
    {
        private readonly FakeFileProbe _inner = new();

        public bool Exists(string path) => false;

        public string ExpandEnvironment(string value) =>
            _inner.ExpandEnvironment(value.Replace("%LOCALAPPDATA%", Local, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<string> ListFiles(string directory) => [];

        public byte[]? ReadSmallFile(string path, int maxBytes) => null;
    }

    internal static CleanupEngine Engine(FakeFileSystem fs, FakeRecycleBin? bin = null, CleanupCatalog? catalog = null) =>
        new(catalog ?? CleanupCatalog.Default, fs, bin ?? new FakeRecycleBin(0, 0), new Probe(), new Profiles(), new Clock());

    [Fact]
    public void Analyze_TempIgnoresRecentFiles()
    {
        var fs = new FakeFileSystem()
            .File(@"C:\Windows\Temp\old.tmp", 50 * MB, Now.AddDays(-3))
            .File(@"C:\Windows\Temp\sub\old2.tmp", 10 * MB, Now.AddDays(-2))
            .File(@"C:\Windows\Temp\fresh.tmp", 99 * MB, Now.AddHours(-1));

        var temp = Engine(fs).Analyze().Categories.Single(c => c.Id == "windows-temp");

        temp.Bytes.Should().Be(60 * MB);
        temp.Files.Should().Be(2);
    }

    [Fact]
    public void Analyze_WildcardExpandsBrowserProfiles()
    {
        var fs = new FakeFileSystem()
            .File($@"{Local}\Google\Chrome\User Data\Default\Cache\Cache_Data\f_1", 5 * MB, Now)
            .File($@"{Local}\Google\Chrome\User Data\Profile 1\Cache\Cache_Data\f_2", 7 * MB, Now);

        Engine(fs).Analyze().Categories.Single(c => c.Id == "chrome-cache").Bytes.Should().Be(12 * MB);
    }

    [Fact]
    public void Analyze_IndividualFiles_CrashDump()
    {
        var fs = new FakeFileSystem().File(@"C:\Windows\MEMORY.DMP", 800 * MB, Now.AddDays(-5));

        var dumps = Engine(fs).Analyze().Categories.Single(c => c.Id == "crash-dumps");

        dumps.Bytes.Should().Be(800 * MB);
        dumps.Risk.Should().Be(RiskLevel.Conditional);
    }

    [Theory]
    [InlineData(@"%SystemRoot%\..\Users\Lea\Documents")]
    [InlineData(@"C:\Users\Lea\Documents")]
    [InlineData(@"%LOCALAPPDATA%\..\Roaming")]
    [InlineData(@"%SystemRoot%")]
    [InlineData(@"\\server\share\x")]
    public void UnsafeRoots_AreRefused(string root)
    {
        var json = $$"""{ "version": 1, "categories": [ { "id": "evil", "roots": ["{{root.Replace("\\", "\\\\", StringComparison.Ordinal)}}"], "minAgeHours": 0, "risk": "Safe" } ] }""";
        var fs = new FakeFileSystem().File(@"C:\Users\Lea\Documents\thesis.docx", 1 * MB, Now.AddDays(-30))
            .File(@"C:\Users\Lea\AppData\Roaming\x.dat", 1 * MB, Now.AddDays(-30)).File(@"C:\Windows\explorer.exe", 1 * MB, Now.AddDays(-30));
        var engine = Engine(fs, catalog: CleanupCatalog.Load(json));

        engine.Analyze().Categories.Single().Bytes.Should().Be(0);
        engine.Clean("evil");
        fs.Deleted.Should().BeEmpty();
    }

    [Fact]
    public void Clean_DeletesOnlyEligibleFiles_SkipsLocked_RemovesEmptyDirs()
    {
        var fs = new FakeFileSystem()
            .File(@"C:\Windows\Temp\a.tmp", 10 * MB, Now.AddDays(-3))
            .File(@"C:\Windows\Temp\locked.tmp", 10 * MB, Now.AddDays(-3))
            .File(@"C:\Windows\Temp\fresh.tmp", 10 * MB, Now);
        fs.Locked.Add(@"C:\Windows\Temp\locked.tmp");

        var freed = Engine(fs).Clean("windows-temp");

        fs.Deleted.Should().Equal(@"C:\Windows\Temp\a.tmp");
        freed.Should().Be(10 * MB);
        fs.EmptiedRoots.Should().Equal(@"C:\Windows\Temp");
    }

    [Fact]
    public void Clean_NeverDeletesThroughLinks()
    {
        var fs = new FakeFileSystem()
            .File($@"{Local}\Temp\junk.tmp", 1 * MB, Now.AddDays(-2))
            .File($@"{Local}\Temp\link\precious.docx", 1 * MB, Now.AddDays(-2));
        fs.Links.Add($@"{Local}\Temp\link");

        Engine(fs).Clean("user-temp");

        fs.Deleted.Should().Equal($@"{Local}\Temp\junk.tmp");
    }

    [Fact]
    public void RecycleBin_QueryAndEmpty()
    {
        var bin = new FakeRecycleBin(2048 * MB, 40);
        var engine = Engine(new FakeFileSystem(), bin);

        engine.Analyze().RecycleBinBytes.Should().Be(2048 * MB);
        engine.Clean(CleanupEngine.RecycleBinId).Should().Be(2048 * MB);
        bin.Emptied.Should().BeTrue();
    }

    [Fact]
    public void BigFiles_Top_InUserFolders()
    {
        var fs = new FakeFileSystem()
            .File(@"C:\Users\Lea\Downloads\movie.mkv", 4000 * MB, Now)
            .File(@"C:\Users\Lea\Videos\holiday.mp4", 900 * MB, Now)
            .File(@"C:\Users\Lea\Documents\small.pdf", 2 * MB, Now)
            .File(@"C:\Users\Lea\AppData\Local\big.cache", 9000 * MB, Now);

        Engine(fs).BigFiles().Select(f => f.Path).Should().Equal(@"C:\Users\Lea\Downloads\movie.mkv", @"C:\Users\Lea\Videos\holiday.mp4");
    }

    [Fact]
    public void Catalog_FileParses_AllCategoriesHaveTexts()
    {
        var catalog = CleanupCatalog.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Definitions", "cleanup.json")));

        catalog.Categories.Should().HaveCountGreaterThan(8);
        foreach (var culture in new[] { global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.CultureInfo.GetCultureInfo("fr") })
        {
            var set = CleanupModule.Resources.GetResourceSet(culture, true, false)!;
            catalog.Categories.Concat([new CleanupCategory(CleanupEngine.RecycleBinId, [], [], 0, RiskLevel.Conditional), new CleanupCategory(CleanupEngine.ComponentStoreId, [], [], 0, RiskLevel.Safe)])
                .Should().OnlyContain(c => set.GetString($"Cleanup.{c.Id}.Title") != null && set.GetString($"Cleanup.{c.Id}.Explanation") != null, culture.Name);
        }
    }
}
