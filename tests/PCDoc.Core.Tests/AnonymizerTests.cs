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
}
