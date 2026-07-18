using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Safety;
using AntiLagNext.Infrastructure.Tweaks;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Infrastructure.Tests;

public class TweakCatalogTests
{
    [Fact]
    public void All_ids_are_unique()
    {
        var ids = TweakCatalog.All.Select(t => t.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotBeEmpty();
    }

    [Fact]
    public void All_catalog_paths_pass_registry_policy()
    {
        foreach (var t in TweakCatalog.All)
        {
            RegistryPathPolicy.IsSafeRegistryPath(t.Hive, t.KeyPath, t.ValueName)
                .Should().BeTrue($"{t.Id} path must be allowlisted");
        }
    }

    [Fact]
    public void ForProfile_Gaming_and_Max_only_Safe_or_Moderate()
    {
        foreach (var kind in new[] { ProfileKind.Gaming, ProfileKind.MaxPerformance })
        {
            var list = TweakCatalog.ForProfile(kind);
            list.Should().NotBeEmpty();
            list.Should().OnlyContain(t =>
                t.Risk == TweakRisk.Safe || t.Risk == TweakRisk.Moderate);
            list.Should().OnlyContain(t => t.Profiles.Contains(kind));
        }
    }

    [Fact]
    public void ForProfile_Office_is_subset()
    {
        var office = TweakCatalog.ForProfile(ProfileKind.Office);
        var gaming = TweakCatalog.ForProfile(ProfileKind.Gaming);
        office.Count.Should().BeLessThan(gaming.Count);
        office.Should().OnlyContain(t =>
            t.Risk == TweakRisk.Safe || t.Risk == TweakRisk.Moderate);
    }

    [Fact]
    public void ForProfile_Default_is_empty()
    {
        TweakCatalog.ForProfile(ProfileKind.Default).Should().BeEmpty();
        TweakCatalog.ForProfile(ProfileKind.Custom).Should().BeEmpty();
    }

    [Fact]
    public void Expected_catalog_ids_exist()
    {
        string[] required =
        {
            "latency.interrupt_steering",
            "latency.serialize_timer",
            "cpu.win32_priority_separation",
            "network.throttling_index",
            "network.no_lazy_mode",
            "network.tcp_ack_frequency",
            "network.tcp_no_delay",
            "input.mouse_queue",
            "input.keyboard_queue",
            "input.mouse_accel_off",
            "power.throttling_off",
            "mmcss.lazy_mode_timeout"
        };

        foreach (var id in required)
            TweakCatalog.GetById(id).Should().NotBeNull(id);
    }
}
