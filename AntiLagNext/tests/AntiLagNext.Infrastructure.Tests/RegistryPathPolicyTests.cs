using AntiLagNext.Infrastructure.Safety;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Infrastructure.Tests;

public class RegistryPathPolicyTests
{
    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "InterruptSteeringDisabled")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "SerializeTimerExpiration")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Power", "PowerThrottlingOff")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode")]
    public void Allows_latency_tweak_prefixes(string path, string value)
    {
        RegistryPathPolicy.IsSafeRegistryPath("HKLM", path, value).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Evil", "Start")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Evil\Parameters", "x")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\SysMain\Parameters", "x")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "Shell")]
    public void Denies_arbitrary_services_and_dangerous_paths(string path, string value)
    {
        RegistryPathPolicy.IsSafeRegistryPath("HKLM", path, value).Should().BeFalse();
    }
}
