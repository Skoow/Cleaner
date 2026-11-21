using PCDoc.Core.Bilan;
using PCDoc.Core.Findings;

namespace PCDoc.Core.Tests;

public class HealthScoreTests
{
    private static Finding F(Severity s) => new("x", "m", s, new LocalizedText("T"), new LocalizedText("E"), "");

    [Fact]
    public void Score_NoFindings_Is100() => HealthScore.Compute([]).Should().Be(100);

    [Fact]
    public void Score_OneCriticalTwoWarnings_Is70() =>
        HealthScore.Compute([F(Severity.Critical), F(Severity.Warning), F(Severity.Warning), F(Severity.Info)])
            .Should().Be(70);

    [Fact]
    public void Score_NeverBelowZero() =>
        HealthScore.Compute(Enumerable.Repeat(F(Severity.Critical), 6)).Should().Be(0);

    [Theory]
    [InlineData(100, "Score.Good")]
    [InlineData(80, "Score.Good")]
    [InlineData(79, "Score.Watch")]
    [InlineData(50, "Score.Watch")]
    [InlineData(49, "Score.Improve")]
    public void LabelKey_FollowsThresholds(int score, string key) => HealthScore.LabelKey(score).Should().Be(key);
}
