using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Settings;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Settings;

/// <summary>
/// Тесты настроек приложения: создание по умолчанию, выбор активного профиля, устойчивость.
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void CreateDefault_HasThreePresets()
    {
        var settings = AppSettings.CreateDefault();

        settings.Profiles.Should().HaveCount(3);
        settings.Profiles.Should().Contain(p => p.Kind == ProfileKind.Default);
        settings.Profiles.Should().Contain(p => p.Kind == ProfileKind.Gaming);
        settings.Profiles.Should().Contain(p => p.Kind == ProfileKind.Office);
    }

    [Fact]
    public void CreateDefault_ActiveProfileIsDefault()
    {
        var settings = AppSettings.CreateDefault();

        var active = settings.GetActiveProfile();
        active.Kind.Should().Be(ProfileKind.Default);
    }

    [Fact]
    public void GetActiveProfile_FallsBackToDefault_WhenIdUnknown()
    {
        var settings = AppSettings.CreateDefault();
        settings.ActiveProfileId = Guid.NewGuid(); // несуществующий

        var active = settings.GetActiveProfile();
        active.Kind.Should().Be(ProfileKind.Default, "при битой ссылке возвращается профиль по умолчанию");
    }

    [Fact]
    public void GetActiveProfile_ReturnsSelectedProfile()
    {
        var settings = AppSettings.CreateDefault();
        var gaming = settings.Profiles.First(p => p.Kind == ProfileKind.Gaming);
        settings.ActiveProfileId = gaming.Id;

        var active = settings.GetActiveProfile();
        active.Id.Should().Be(gaming.Id);
    }
}
