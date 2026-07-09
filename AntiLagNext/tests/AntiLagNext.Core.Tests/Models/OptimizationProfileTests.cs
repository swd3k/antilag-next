using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Models;

/// <summary>
/// Тесты предустановленных профилей оптимизации (логика выбора параметров).
/// Чистые unit-тесты — без Win32, без UI.
/// </summary>
public class OptimizationProfileTests
{
    [Theory]
    [InlineData(ProfileKind.Gaming,   "Игровой")]
    [InlineData(ProfileKind.Office,   "Офисный")]
    [InlineData(ProfileKind.Default,  "По умолчанию")]
    public void CreatePreset_HasCorrectName(ProfileKind kind, string expectedName)
    {
        var profile = OptimizationProfile.CreatePreset(kind);

        profile.Name.Should().Be(expectedName);
        profile.Kind.Should().Be(kind);
        profile.Description.Should().NotBeEmpty("у каждого профиля должно быть описание для пользователя");
    }

    [Fact]
    public void GamingPreset_EnablesAggressiveOptimizations()
    {
        var profile = OptimizationProfile.CreatePreset(ProfileKind.Gaming);

        profile.EnableTimer.Should().BeTrue();
        profile.TimerTargetMs.Should().Be(0.5);
        profile.EnablePowerScheme.Should().BeTrue();
        profile.EnableCoreParkingControl.Should().BeTrue();
        profile.CoreParkingMode.Should().Be(CoreParkingMode.AllActive);
        profile.EnableGameModeTweak.Should().BeTrue();
        profile.EnableHags.Should().BeTrue();
    }

    [Fact]
    public void OfficePreset_IsConservative_AndKeepsEfficientIdle()
    {
        var profile = OptimizationProfile.CreatePreset(ProfileKind.Office);

        profile.TimerTargetMs.Should().Be(1.0);
        profile.CoreParkingMode.Should().Be(CoreParkingMode.KeepEfficientIdle,
            "офисный профиль не должен трогать E-cores");
        profile.EnableHags.Should().BeFalse();
        profile.EnableGameModeTweak.Should().BeFalse();
    }

    [Fact]
    public void DefaultPreset_DisablesEverything()
    {
        var profile = OptimizationProfile.CreatePreset(ProfileKind.Default);

        profile.EnableTimer.Should().BeFalse();
        profile.EnablePowerScheme.Should().BeFalse();
        profile.EnableCoreParkingControl.Should().BeFalse();
        profile.EnableMemoryCleanup.Should().BeFalse();
    }

    [Fact]
    public void CreatePreset_AlwaysAssignsUniqueIds()
    {
        var a = OptimizationProfile.CreatePreset(ProfileKind.Gaming);
        var b = OptimizationProfile.CreatePreset(ProfileKind.Gaming);

        a.Id.Should().NotBeEmpty();
        b.Id.Should().NotBeEmpty();
        a.Id.Should().NotBe(b.Id, "каждый профиль получает уникальный GUID");
    }
}
