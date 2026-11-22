using PCDoc.Core.Abstractions;
using PCDoc.Core.Activity;

namespace PCDoc.Core.Tests;

public sealed class ActivityLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "pcdoc-activity-" + Guid.NewGuid().ToString("N") + ".txt");

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; } = new(2026, 11, 22, 14, 3, 12, TimeSpan.Zero);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Write_OneLinePerAction_WithDateAndCategory()
    {
        var log = new TextActivityLog(_path, new Clock());

        log.Write("cleanup", "Done: temporary files\nsecond line");
        log.Write("apps", "Done: uninstall Contoso");

        var lines = log.ReadAll();
        lines.Should().HaveCount(2);
        lines[0].Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  \[cleanup\]  Done: temporary files \| second line$");
        var report = ActivityReport.Build(lines, "PCDoc", "0.8", "PC-LEA", "Windows 11", new Clock().Now);
        report.Should().Contain("PCDoc 0.8").And.Contain("PC-LEA").And.Contain("[apps]  Done: uninstall Contoso");
    }
}
