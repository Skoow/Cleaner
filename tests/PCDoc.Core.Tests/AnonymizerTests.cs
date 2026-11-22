using PCDoc.Core.Activity;

namespace PCDoc.Core.Tests;

public sealed class AnonymizerTests
{
    [Fact]
    public void Apply_RemovesPersonalInformation_KeepsUsefulDetails()
    {
        var text = @"PC-LEA: C:\Users\lea.martin\AppData\Local\Temp\x.exe from 192.168.1.20 (MAC 00-1A-2B-3C-4D-5E), contact lea@example.com, " +
                   @"S-1-5-21-111-222-333-1001, SerialNumber: ABC123XYZ, KB5040001 {11111111-2222-3333-4444-555555555555} 127.0.0.1";

        var result = Anonymizer.Apply(text, ["PC-LEA", "lea.martin"]);

        result.Should().NotContain("PC-LEA").And.NotContain("lea.martin").And.NotContain("192.168.1.20").And.NotContain("00-1A-2B")
            .And.NotContain("lea@example.com").And.NotContain("111-222-333").And.NotContain("ABC123XYZ");
        result.Should().Contain(@"C:\Users\<utilisateur>\AppData\Local\Temp\x.exe").And.Contain("KB5040001")
            .And.Contain("{11111111-2222-3333-4444-555555555555}").And.Contain("127.0.0.1");
    }

    [Fact]
    public void Apply_MasksWholeWordsOnly_AndCoversNetworkIdentifiers()
    {
        var text = "User Max: Maximum Automatic Administrators. C:\\Users\\Max : 12 GB free. Mail max@contoso.com. \\\\NAS-MARTIN\\share. " +
                   "fe80::1c2d:3e4f:5a6b:7c8d%12 and 2a01:e0a:1:2:3:4:5:6 at 10:20:30, ::1. Key ABCDE-FGHIJ-KLMNO-PQRST-UVWXY. " +
                   "S-1-12-1-111-222-333-444. Volume Serial Number is 1234-ABCD";

        var result = Anonymizer.Apply(text, ["Max", "C:\\Users\\Max"]);

        result.Should().Contain("Maximum Automatic Administrators").And.Contain("12 GB free").And.Contain("10:20:30").And.Contain("::1");
        result.Should().NotContain("Max:").And.NotContain("contoso").And.NotContain("NAS-MARTIN").And.NotContain("fe80").And.NotContain("2a01")
            .And.NotContain("ABCDE-FGHIJ").And.NotContain("111-222").And.NotContain("1234-ABCD");
    }
}
