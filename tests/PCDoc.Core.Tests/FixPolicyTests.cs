using PCDoc.Core.Findings;
using PCDoc.Core.Settings;

namespace PCDoc.Core.Tests;

public class FixPolicyTests
{
    private static Finding F(RiskLevel risk, bool fix = true, bool reversible = true) =>
        new("x", "m", Severity.Info, new LocalizedText("T"), new LocalizedText("E"), "", fix, risk, reversible);

    [Theory]
    [InlineData(RiskLevel.Safe, AppMode.Novice, true)]
    [InlineData(RiskLevel.Conditional, AppMode.Novice, false)]
    [InlineData(RiskLevel.Discouraged, AppMode.Novice, false)]
    [InlineData(RiskLevel.Locked, AppMode.Novice, false)]
    [InlineData(RiskLevel.Safe, AppMode.Expert, true)]
    [InlineData(RiskLevel.Conditional, AppMode.Expert, true)]
    [InlineData(RiskLevel.Discouraged, AppMode.Expert, true)]
    [InlineData(RiskLevel.Locked, AppMode.Expert, false)]
    public void IsOffered_FollowsModeAndRisk(RiskLevel risk, AppMode mode, bool expected) =>
        FixPolicy.IsOffered(F(risk), mode).Should().Be(expected);

    [Fact]
    public void IsOffered_NoFix_False() => FixPolicy.IsOffered(F(RiskLevel.Safe, fix: false), AppMode.Expert).Should().BeFalse();

    [Theory]
    [InlineData(RiskLevel.Safe, true)]
    [InlineData(RiskLevel.Conditional, false)]
    [InlineData(RiskLevel.Discouraged, false)]
    public void FixAll_SafeOnly(RiskLevel risk, bool expected) => FixPolicy.IsInFixAll(F(risk)).Should().Be(expected);

    [Fact]
    public void StrongConfirmation_ForDiscouragedOrIrreversible()
    {
        FixPolicy.NeedsStrongConfirmation(F(RiskLevel.Discouraged)).Should().BeTrue();
        FixPolicy.NeedsStrongConfirmation(F(RiskLevel.Conditional, reversible: false)).Should().BeTrue();
        FixPolicy.NeedsStrongConfirmation(F(RiskLevel.Safe, reversible: false)).Should().BeFalse("réparation sûre non annulable par nature");
        FixPolicy.NeedsStrongConfirmation(F(RiskLevel.Safe)).Should().BeFalse();
    }

    [Fact]
    public void Settings_DefaultModeIsNovice_FirstRunNotDone()
    {
        var s = AppSettings.DefaultsFor(System.Globalization.CultureInfo.InvariantCulture);

        s.Mode.Should().Be(AppMode.Novice);
        s.FirstRunCompleted.Should().BeFalse();
    }
}
