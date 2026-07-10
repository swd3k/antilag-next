using AntiLagNext.Core.Settings;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Settings;

public class AutoApplyPolicyTests
{
    [Theory]
    // First launch / never enabled
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, true, true, false)]
    // User enabled once but left OFF (Reset/Disable)
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, false, true, false)]
    // Left ON but auto-apply preference off (interactive)
    [InlineData(true, false, true, false, false)]
    // Happy path: enabled + preference + left on
    [InlineData(true, true, true, false, true)]
    // Logon launcher: still needs left-on + user opt-in
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, true, true, true)]
    public void ShouldAutoApplyOnStart_MatchesProductRules(
        bool userEnabled,
        bool autoFlag,
        bool leftOn,
        bool autostart,
        bool expected)
    {
        AutoApplyPolicy.ShouldAutoApplyOnStart(userEnabled, autoFlag, leftOn, autostart)
            .Should().Be(expected);
    }

    [Fact]
    public void DescribeSkipReason_ExplainsFirstUse()
    {
        var msg = AutoApplyPolicy.DescribeSkipReason(false, true, true, false, english: true);
        msg.Should().NotBeNullOrWhiteSpace();
        msg.Should().Contain("not enabled");
    }

    [Fact]
    public void DescribeSkipReason_ExplainsLeftOff()
    {
        var msg = AutoApplyPolicy.DescribeSkipReason(true, true, false, false, english: false);
        msg.Should().Contain("выключена");
    }

    [Fact]
    public void DescribeSkipReason_NullWhenShouldApply()
    {
        AutoApplyPolicy.DescribeSkipReason(true, true, true, false, english: true)
            .Should().BeNull();
    }
}
