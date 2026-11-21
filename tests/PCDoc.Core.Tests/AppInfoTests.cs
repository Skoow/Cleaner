using PCDoc.Core;

namespace PCDoc.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void AppInfo_ReadsProductNameFromBuildProps()
    {
        AppInfo.ProductName.Should().Be("PCDoc");
        AppInfo.DataFolderName.Should().Be(AppInfo.ProductName);
    }

    [Fact]
    public void AppInfo_Version_HasNoCommitSuffix()
    {
        AppInfo.Version.Should().Be("0.4.0");
    }
}
