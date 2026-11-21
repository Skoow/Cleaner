using PCDoc.Modules.System.Boot;
using PCDoc.Modules.System.Tests.Fakes;
using static PCDoc.Modules.System.Tests.Fakes.FakeEventLogQuery;

namespace PCDoc.Modules.System.Tests;

public class BootPerformanceReaderTests
{
    private static readonly DateTimeOffset Day0 = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private static global::PCDoc.Core.Abstractions.EventRecordData Boot(int day, int totalMs, int mainMs = 30_000, int postMs = 10_000) =>
        Event(100, Day0.AddDays(day), ("BootTime", $"{totalMs}"), ("MainPathBootTime", $"{mainMs}"), ("BootPostBootTime", $"{postMs}"));

    private static global::PCDoc.Core.Abstractions.EventRecordData Degradation(int id, int day, int minute, string name, int degradationMs) =>
        Event(id, Day0.AddDays(day).AddMinutes(minute), ("Name", name), ("FriendlyName", name + " friendly"),
            ("TotalTime", $"{degradationMs + 1000}"), ("DegradationTime", $"{degradationMs}"));

    [Fact]
    public void Read_ParsesBootTimes()
    {
        var log = new FakeEventLogQuery(Boot(0, 71_204, 40_000, 31_204));

        var boot = new BootPerformanceReader(log).Read().Boots.Single();

        boot.Total.Should().Be(TimeSpan.FromMilliseconds(71_204));
        boot.MainPath.Should().Be(TimeSpan.FromMilliseconds(40_000));
        boot.PostBoot.Should().Be(TimeSpan.FromMilliseconds(31_204));
        log.LastLog.Should().Be("Microsoft-Windows-Diagnostics-Performance/Operational");
    }

    [Fact]
    public void Read_AttachesDegradationsToPrecedingBoot()
    {
        var log = new FakeEventLogQuery(
            Boot(0, 60_000), Degradation(102, 0, 1, "nvlddmkm.sys", 6_212),
            Boot(1, 61_000), Degradation(103, 1, 2, "Spooler", 5_418), Degradation(101, 1, 3, "Discord.exe", 3_100));

        var boots = new BootPerformanceReader(log).Read().Boots;

        boots.Should().HaveCount(2);
        boots[0].Date.Should().Be(Day0.AddDays(1)); // le plus récent d'abord
        boots[0].Degradations.Select(d => d.Name).Should().BeEquivalentTo("Spooler", "Discord.exe");
        boots[1].Degradations.Single().Name.Should().Be("nvlddmkm.sys");
        boots[1].Degradations.Single().DegradationTime.Should().Be(TimeSpan.FromMilliseconds(6_212));
    }

    [Theory]
    [InlineData(101, DegradationKind.Application)]
    [InlineData(102, DegradationKind.Driver)]
    [InlineData(103, DegradationKind.Service)]
    [InlineData(106, DegradationKind.Other)]
    public void Read_MapsEventIdToKind(int id, DegradationKind kind)
    {
        var log = new FakeEventLogQuery(Boot(0, 60_000), Degradation(id, 0, 1, "x", 1_000));

        new BootPerformanceReader(log).Read().Boots.Single().Degradations.Single().Kind.Should().Be(kind);
    }

    [Fact]
    public void Read_WhenNoEvents_ReturnsEmptyHistory()
    {
        var history = new BootPerformanceReader(new FakeEventLogQuery()).Read();

        history.Boots.Should().BeEmpty();
        history.Median(5).Should().BeNull();
        history.Trend.Should().Be(BootTrend.Unknown);
    }

    [Fact]
    public void Read_SkipsEventWithMissingField()
    {
        var log = new FakeEventLogQuery(Event(100, Day0, ("MainPathBootTime", "1000")), Boot(1, 50_000));

        new BootPerformanceReader(log).Read().Boots.Should().ContainSingle();
    }

    [Fact]
    public void Read_DegradationBeforeAnyBoot_IsIgnored()
    {
        var log = new FakeEventLogQuery(Degradation(101, 0, -5, "early", 1_000), Boot(0, 50_000));

        new BootPerformanceReader(log).Read().Boots.Single().Degradations.Should().BeEmpty();
    }

    [Fact]
    public void Median_OfLast5()
    {
        var log = new FakeEventLogQuery(
            Boot(0, 10_000), Boot(1, 71_204), Boot(2, 74_019), Boot(3, 69_880), Boot(4, 78_412), Boot(5, 74_950));

        new BootPerformanceReader(log).Read().Median(5).Should().Be(TimeSpan.FromMilliseconds(74_019));
    }

    [Fact]
    public void Trend_Up_WhenRecentMedianIs20PercentHigher()
    {
        var events = Enumerable.Range(0, 5).Select(d => Boot(d, 50_000))
            .Concat(Enumerable.Range(5, 5).Select(d => Boot(d, 60_000))).ToArray();

        new BootPerformanceReader(new FakeEventLogQuery(events)).Read().Trend.Should().Be(BootTrend.Up);
    }

    [Fact]
    public void Trend_Stable_WithinTenPercent()
    {
        var events = Enumerable.Range(0, 10).Select(d => Boot(d, 50_000 + (d * 200))).ToArray();

        new BootPerformanceReader(new FakeEventLogQuery(events)).Read().Trend.Should().Be(BootTrend.Stable);
    }
}
