using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Settings;

public class AppSettingsMigrationTests
{
    [Fact]
    public void Migrate_RenamesLegacyRussianBuiltInNames_ToEnglish()
    {
        var s = new AppSettings
        {
            SchemaVersion = 1,
            Profiles =
            {
                new OptimizationProfile
                {
                    Kind = ProfileKind.Gaming,
                    Name = "Игровой",
                    Description = "Максимальная отзывчивость: таймер 0.5 мс"
                },
                new OptimizationProfile
                {
                    Kind = ProfileKind.Office,
                    Name = "Офисный",
                    Description = "Мягкие настройки для повседневной работы"
                },
                new OptimizationProfile
                {
                    Kind = ProfileKind.Default,
                    Name = "По умолчанию",
                    Description = "Система в исходном состоянии"
                }
            }
        };

        bool dirty = s.MigrateToCurrentSchema();

        dirty.Should().BeTrue();
        s.SchemaVersion.Should().Be(AppSettings.CurrentSchemaVersion);
        s.Profiles.Should().Contain(p => p.Kind == ProfileKind.MaxPerformance);
        s.Profiles.First(p => p.Kind == ProfileKind.Gaming).Name.Should().Be("Gaming");
        s.Profiles.First(p => p.Kind == ProfileKind.Office).Name.Should().Be("Office");
        s.Profiles.First(p => p.Kind == ProfileKind.Default).Name.Should().Be("Default");
        s.Profiles.First(p => p.Kind == ProfileKind.Gaming).Description
            .Should().NotContain("Максимальная отзывчивость");
    }

    [Fact]
    public void Migrate_IsIdempotent_WhenAlreadyCurrent()
    {
        var s = AppSettings.CreateDefault();
        s.SchemaVersion = AppSettings.CurrentSchemaVersion;

        bool first = s.MigrateToCurrentSchema();
        bool second = s.MigrateToCurrentSchema();

        // CreateDefault is already schema-current → both false
        second.Should().BeFalse("second migrate must not dirty settings again");
        first.Should().BeFalse();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Gaming", "Office", "Default", "Maximum performance", "Custom"
        };
        foreach (var n in s.Profiles.Select(p => p.Name))
        {
            (allowed.Contains(n) || n.StartsWith("Custom", StringComparison.Ordinal))
                .Should().BeTrue($"unexpected profile name: {n}");
        }
    }

    [Fact]
    public void LocalizedName_RespectsCulture()
    {
        OptimizationProfile.LocalizedName(ProfileKind.Gaming, "en").Should().Be("Gaming");
        OptimizationProfile.LocalizedName(ProfileKind.Gaming, "ru").Should().Be("Игровой");
        OptimizationProfile.LocalizedName(ProfileKind.Office, "en").Should().Be("Office");
        OptimizationProfile.I18nKey(ProfileKind.MaxPerformance).Should().Be("profile.max");
    }
}
