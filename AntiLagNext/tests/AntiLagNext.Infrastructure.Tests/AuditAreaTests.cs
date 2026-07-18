using AntiLagNext.Infrastructure.Tweaks;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Infrastructure.Tests;

public class AuditAreaTests
{
    [Theory]
    [InlineData("audit.hags", null, null, "Gpu")]
    [InlineData("audit.network_throttling", "network.throttling_index", null, "Network")]
    [InlineData("audit.tcp_ack", "network.tcp_ack_frequency", null, "Network")]
    [InlineData("audit.serialize_timer", "latency.serialize_timer", null, "Timer")]
    [InlineData("audit.interrupt_steering", "latency.interrupt_steering", null, "Timer")]
    [InlineData("audit.power_throttling", "power.throttling_off", null, "Power")]
    [InlineData("audit.mouse_queue", "input.mouse_queue", null, "Input")]
    [InlineData("audit.keyboard_queue", "input.keyboard_queue", null, "Input")]
    [InlineData("audit.win32_priority", "cpu.win32_priority_separation", null, "System")]
    [InlineData("audit.active_state", null, null, "System")]
    public void MapArea_by_id_and_tweak(string id, string? tweak, string? path, string expected)
    {
        AuditService.MapArea(id, tweak, path).Should().Be(expected);
    }
}
