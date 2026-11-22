using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Cleanup.Tests;

public sealed class SecureEraseTests
{
    private sealed class Eraser : ISecureEraser
    {
        public List<string> Wiped { get; } = [];

        public IReadOnlyList<string> Expand(string path) => path.EndsWith(".docx", StringComparison.Ordinal) ? [path] : [path + @"\a.txt", path + @"\b.txt"];

        public bool Wipe(string file)
        {
            Wiped.Add(file);
            return true;
        }

        public void RemoveEmptyFolders(string folder)
        {
        }

        public bool IsAllowed(string path) => !path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase);

        public List<string> CipherCleaned { get; } = [];

        public void RemoveCipherLeftovers(string drive) => CipherCleaned.Add(drive);
    }

    private sealed class Runner : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public int Exit { get; set; }

        public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            Calls.Add($"{system32Tool} {arguments}");
            return Task.FromResult(Exit);
        }
    }

    [Fact]
    public async Task Erase_IsConditionalIrreversible_AndRefusesProtectedPaths()
    {
        var eraser = new Eraser();
        var runner = new Runner();
        var fixer = new SecureEraseFixer(eraser, runner);
        var finding = SecureEraseFixer.ForPaths([@"D:\Docs\secret.docx", @"D:\Old"]);

        finding.Risk.Should().Be(RiskLevel.Conditional);
        FixPolicy.NeedsStrongConfirmation(finding).Should().BeTrue();
        fixer.CanFix(SecureEraseFixer.ForPaths([@"C:\Windows\System32\drivers"])).Should().BeFalse();
        fixer.CanFix(finding with { Risk = RiskLevel.Safe }).Should().BeFalse();

        await fixer.ApplyAsync(finding, CancellationToken.None);
        eraser.Wiped.Should().Equal(@"D:\Docs\secret.docx", @"D:\Old\a.txt", @"D:\Old\b.txt");

        await fixer.ApplyAsync(SecureEraseFixer.ForFreeSpace(@"D:\"), CancellationToken.None);
        runner.Calls.Should().Equal(@"cipher.exe /w:D:\");
        fixer.CanFix(SecureEraseFixer.ForFreeSpace("D:\\\" & del")).Should().BeFalse();
    }

    [Fact]
    public async Task FreeSpace_InterruptedCipher_RemovesItsTemporaryFolder()
    {
        var eraser = new Eraser();
        var fixer = new SecureEraseFixer(eraser, new Runner { Exit = 1 });

        var act = () => fixer.ApplyAsync(SecureEraseFixer.ForFreeSpace(@"E:\"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        eraser.CipherCleaned.Should().Equal(@"E:\");
        fixer.NeedsRestorePoint.Should().BeFalse("un cliché du disque garderait une copie des fichiers effacés");
    }
}
