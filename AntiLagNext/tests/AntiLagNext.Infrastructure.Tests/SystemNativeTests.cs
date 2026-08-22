using AntiLagNext.Infrastructure.Native;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Infrastructure.Tests;

public class SystemNativeTests
{
    [Fact]
    public void Exe_rejects_path_injection()
    {
        FluentActions.Invoking(() => SystemNative.Exe(@"..\..\evil.exe")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SystemNative.Exe("powercfg.exe&calc")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SystemNative.Exe("")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SystemNative.Exe("ipconfig.exe|whoami")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Exe_plain_name_is_system32_qualified()
    {
        string path = SystemNative.Exe("schtasks.exe");
        path.Should().EndWith("schtasks.exe");
        path.Should().NotContain("..");
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrEmpty(system))
            path.Should().Be(Path.Combine(system, "schtasks.exe"));
    }
}
