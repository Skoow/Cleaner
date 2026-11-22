using PCDoc.Core.AppUpdates;

namespace PCDoc.Core.Tests;

public sealed class ReleaseFeedTests
{
    private const string Json = """
        {
          "tag_name": "v1.2.0", "draft": false, "prerelease": false,
          "html_url": "https://github.com/Skoow/Cleaner/releases/tag/v1.2.0",
          "assets": [
            { "name": "PCDoc-1.2.0-x64.msi", "browser_download_url": "https://github.com/Skoow/Cleaner/releases/download/v1.2.0/PCDoc-1.2.0-x64.msi" },
            { "name": "PCDoc-1.2.0-arm64.msi", "browser_download_url": "https://evil.example/PCDoc-1.2.0-arm64.msi" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/Skoow/Cleaner/releases/download/v1.2.0/SHA256SUMS.txt" }
          ]
        }
        """;

    [Fact]
    public void Parse_FindsTheInstallerOfTheOfficialRepository_Only()
    {
        var x64 = ReleaseFeed.Parse(Json, new Version(1, 0, 0), "x64");
        x64!.Version.Should().Be(new Version(1, 2, 0));
        x64.InstallerUrl.Should().EndWith("/PCDoc-1.2.0-x64.msi");
        x64.ChecksumsUrl.Should().EndWith("/SHA256SUMS.txt");

        ReleaseFeed.Parse(Json, new Version(1, 0, 0), "arm64")!.InstallerUrl.Should().BeNull("un lien hors du dépôt officiel est ignoré");
        ReleaseFeed.Parse(Json, new Version(1, 2, 0), "x64").Should().BeNull("déjà à jour");
        ReleaseFeed.Parse(Json, new Version(1, 2, 0), "x64", currentIsPrerelease: true).Should().NotBeNull("1.2.0 remplace 1.2.0-beta");
        ReleaseFeed.Parse(Json.Replace("\"prerelease\": false", "\"prerelease\": true", StringComparison.Ordinal), new Version(1, 0, 0), "x64").Should().BeNull();
        ReleaseFeed.Parse(Json.Replace("https://github.com/Skoow/Cleaner/releases/tag", "https://github.com/other/repo/releases/tag", StringComparison.Ordinal),
            new Version(1, 0, 0), "x64").Should().BeNull();
    }

    [Fact]
    public void ParseVersion_AndChecksums()
    {
        ReleaseFeed.ParseVersion("v1.0.0-beta+abc").Should().Be(new Version(1, 0, 0));
        ReleaseFeed.ParseVersion("1.3").Should().Be(new Version(1, 3, 0));
        ReleaseFeed.ParseVersion("latest").Should().BeNull();

        var hash = new string('a', 64);
        var sums = $"{new string('b', 64)}  PCDoc-1.2.0-arm64.msi\n{hash} *PCDoc-1.2.0-x64.msi\n";
        ReleaseFeed.Sha256For(sums, "PCDoc-1.2.0-x64.msi").Should().Be(hash);
        ReleaseFeed.Sha256For(sums, "PCDoc-1.2.0-x86.msi").Should().BeNull();
    }
}
