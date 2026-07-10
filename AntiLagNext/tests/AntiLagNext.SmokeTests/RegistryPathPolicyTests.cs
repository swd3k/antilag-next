using AntiLagNext.Infrastructure.Safety;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.SmokeTests;

/// <summary>
/// Security: backup restore must not accept arbitrary HKLM paths from malicious JSON.
/// </summary>
public class RegistryPathPolicyTests
{
    [Theory]
    [InlineData("HKLM", @"SOFTWARE\Microsoft\GameBar", "AppCaptureEnabled", true)]
    [InlineData("HKCU", @"Software\AntiLagNext", "x", true)]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpTimedWaitDelay", true)]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", true)]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", true)]
    public void Allows_known_safe_paths(string hive, string path, string value, bool ok)
    {
        RegistryPathPolicy.IsSafeRegistryPath(hive, path, value).Should().Be(ok);
    }

    [Theory]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Services\WinDefend", "Start")] // not in service allowlist
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Services\SysMain\Parameters", "x")] // nested blocked
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", "x")]
    [InlineData("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "Shell")]
    [InlineData("HKLM", @"SOFTWARE\AMD\Something", "x")] // broad AMD root removed
    [InlineData("HKCR", @"SOFTWARE\AntiLagNext", "x")]
    [InlineData("HKLM", @"..\SOFTWARE\AntiLagNext", "x")]
    public void Rejects_dangerous_paths(string hive, string path, string value)
    {
        RegistryPathPolicy.IsSafeRegistryPath(hive, path, value).Should().BeFalse();
    }

    [Fact]
    public void Rejects_null_in_path()
    {
        RegistryPathPolicy.IsSafeRegistryPath("HKLM", "SOFTWARE\\AntiLagNext\0evil", "x")
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(-1, false)]
    [InlineData(5, false)]
    [InlineData(99, false)]
    public void Service_start_type_range(int t, bool ok)
    {
        RegistryPathPolicy.IsValidServiceStartType(t).Should().Be(ok);
    }
}

