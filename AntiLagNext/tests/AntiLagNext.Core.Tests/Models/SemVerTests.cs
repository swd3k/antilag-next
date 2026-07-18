using AntiLagNext.Core.Models;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Models;

public class SemVerTests
{
    [Theory]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("1.1.0+abc", 1, 1, 0)]
    [InlineData("2.0.0-beta.1", 2, 0, 0)]
    public void TryParse_ok(string text, int maj, int min, int pat)
    {
        SemVer.TryParse(text, out var v).Should().BeTrue();
        v.Major.Should().Be(maj);
        v.Minor.Should().Be(min);
        v.Patch.Should().Be(pat);
    }

    [Fact]
    public void Compare_order()
    {
        SemVer.TryParse("1.1.0", out var a).Should().BeTrue();
        SemVer.TryParse("1.2.0", out var b).Should().BeTrue();
        (b > a).Should().BeTrue();
        (a < b).Should().BeTrue();
        a.CompareTo(a).Should().Be(0);
    }
}
