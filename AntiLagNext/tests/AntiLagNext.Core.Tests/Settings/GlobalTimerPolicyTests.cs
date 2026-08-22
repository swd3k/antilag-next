using AntiLagNext.Core.Settings;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Settings;

public class GlobalTimerPolicyTests
{
    [Theory]
    [InlineData(19041, false)]
    [InlineData(22000, false)]
    [InlineData(22621, true)]
    [InlineData(26100, true)]
    public void IsPerProcessTimerOs_by_build(int build, bool expected)
        => GlobalTimerPolicy.IsPerProcessTimerOs(build).Should().Be(expected);

    [Fact]
    public void Win10_games_inherit_without_registry_key()
    {
        GlobalTimerPolicy.GamesInheritTimer(19045, globalRequestsEnabledAtProcessStart: false)
            .Should().BeTrue();
        GlobalTimerPolicy.ScopeKey(19045, false, timerHeld: true).Should().Be("global");
    }

    [Fact]
    public void Win11_22H2_without_key_needs_reboot_for_games()
    {
        GlobalTimerPolicy.GamesInheritTimer(22621, globalRequestsEnabledAtProcessStart: false)
            .Should().BeFalse();
        GlobalTimerPolicy.ScopeKey(22621, false, timerHeld: true).Should().Be("pendingReboot");
    }

    [Fact]
    public void Win11_with_key_already_on_is_global()
    {
        GlobalTimerPolicy.GamesInheritTimer(26100, globalRequestsEnabledAtProcessStart: true)
            .Should().BeTrue();
        GlobalTimerPolicy.ScopeKey(26100, true, timerHeld: true).Should().Be("global");
    }

    [Fact]
    public void Released_timer_scope_is_released()
        => GlobalTimerPolicy.ScopeKey(22621, false, timerHeld: false).Should().Be("released");
}
