using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Models;

public class ApplyChangeSummaryBuilderTests
{
    [Fact]
    public void FromProfile_Gaming_flags_builds_non_empty_core_items()
    {
        var profile = OptimizationProfile.CreatePreset(ProfileKind.Gaming);

        var summary = ApplyChangeSummaryBuilder.FromProfile(profile);

        summary.ProfileKey.Should().Be("gaming");
        summary.ProfileKind.Should().Be(nameof(ProfileKind.Gaming));
        summary.Items.Should().NotBeEmpty();
        summary.Items.Select(i => i.Id).Should().Contain(new[]
        {
            "timer", "power", "parking", "gameMode", "hags", "gpuLowLatency", "memory"
        });
        summary.Items.Should().OnlyContain(i =>
            i.Risk == "safe" || i.Risk == "moderate" || i.Risk == "aggressive");
        summary.Items.Should().Contain(i => i.Id == "timer" && i.Area == "Timer" && i.TitleKey == "changed.timer");
        summary.Items.Should().Contain(i => i.Id == "hags" && i.RequiresReboot);
        summary.AppliedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void FromProfile_includes_catalog_tweaks_and_plugins()
    {
        var profile = OptimizationProfile.CreatePreset(ProfileKind.Gaming);
        var tweaks = new List<TweakDefinition>
        {
            new()
            {
                Id = "network.throttling_index",
                CategoryId = "network",
                NameKey = "tweak.network_throttling.name",
                DescriptionKey = "tweak.network_throttling.desc",
                Risk = TweakRisk.Safe,
                Hive = "HKLM",
                KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                ValueName = "NetworkThrottlingIndex",
                DesiredValue = unchecked((int)0xFFFFFFFFu),
                RequiresReboot = false
            },
            new()
            {
                Id = "latency.serialize_timer",
                CategoryId = "latency",
                NameKey = "tweak.serialize_timer.name",
                DescriptionKey = "tweak.serialize_timer.desc",
                Risk = TweakRisk.Moderate,
                Hive = "HKLM",
                KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                ValueName = "SerializeTimerExpiration",
                DesiredValue = 1,
                RequiresReboot = true
            }
        };
        var plugins = new[] { "ext.network.qos", "ext.network.hygiene" };

        var summary = ApplyChangeSummaryBuilder.FromProfile(profile, tweaks, plugins);

        summary.Items.Should().Contain(i => i.Id == "tweak.network.throttling_index" && i.Area == "Network" && i.Risk == "safe");
        summary.Items.Should().Contain(i =>
            i.Id == "tweak.latency.serialize_timer" && i.Area == "Timer" && i.RequiresReboot);
        summary.Items.Should().Contain(i => i.Id == "plugin.ext.network.qos" && i.Area == "Plugin");
        summary.Items.Should().Contain(i => i.Id == "plugin.ext.network.hygiene");
    }

    [Fact]
    public void FromProfile_Default_with_no_tweaks_is_empty_items()
    {
        var profile = OptimizationProfile.CreatePreset(ProfileKind.Default);
        var summary = ApplyChangeSummaryBuilder.FromProfile(profile);

        summary.ProfileKey.Should().Be("default");
        summary.Items.Should().BeEmpty();
    }

    [Fact]
    public void MapCategoryToArea_and_MapRisk_cover_known_values()
    {
        ApplyChangeSummaryBuilder.MapCategoryToArea("network").Should().Be("Network");
        ApplyChangeSummaryBuilder.MapCategoryToArea("latency").Should().Be("Timer");
        ApplyChangeSummaryBuilder.MapCategoryToArea("input").Should().Be("Input");
        ApplyChangeSummaryBuilder.MapCategoryToArea("power").Should().Be("Power");
        ApplyChangeSummaryBuilder.MapCategoryToArea("unknown").Should().Be("Other");

        ApplyChangeSummaryBuilder.MapRisk(TweakRisk.Safe).Should().Be("safe");
        ApplyChangeSummaryBuilder.MapRisk(TweakRisk.Moderate).Should().Be("moderate");
        ApplyChangeSummaryBuilder.MapRisk(TweakRisk.Aggressive).Should().Be("aggressive");
        ApplyChangeSummaryBuilder.MapRisk(TweakRisk.Advanced).Should().Be("aggressive");
    }

    [Fact]
    public void OperationResult_Code_optional_param_defaults_null()
    {
        var ok = OperationResult.Ok("done");
        var fail = OperationResult.Fail("nope", code: "partial_apply");
        var okT = OperationResult<int>.Ok(1, "v");
        var failT = OperationResult<int>.Fail("x", code: "apply_failed");

        ok.Code.Should().BeNull();
        fail.Code.Should().Be("partial_apply");
        okT.Code.Should().BeNull();
        failT.Code.Should().Be("apply_failed");
    }
}
