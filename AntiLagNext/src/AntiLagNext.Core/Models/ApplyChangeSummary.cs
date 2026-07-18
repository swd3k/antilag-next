namespace AntiLagNext.Core.Models;

/// <summary>
/// One row in the post-Enable "What changed" summary (stable keys for i18n).
/// </summary>
public sealed class ApplyChangeItem
{
    /// <summary>Stable key e.g. "timer", "power", "tweak.network.throttling_index".</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Timer|Power|Gpu|Game|Network|Input|Service|Plugin|Other</summary>
    public string Area { get; init; } = "Other";

    /// <summary>i18n key e.g. "changed.timer".</summary>
    public string TitleKey { get; init; } = string.Empty;

    /// <summary>Optional English technical detail for logs / advanced UI.</summary>
    public string? Detail { get; init; }

    /// <summary>safe|moderate|aggressive</summary>
    public string Risk { get; init; } = "safe";

    public bool RequiresReboot { get; init; }
}

/// <summary>
/// Snapshot of what a successful (or partial) profile apply actually changed.
/// </summary>
public sealed class ApplyChangeSummary
{
    /// <summary>Stable UI profile key (gaming/office/max/default).</summary>
    public string ProfileKey { get; init; } = string.Empty;

    /// <summary><see cref="Enums.ProfileKind"/> name.</summary>
    public string ProfileKind { get; init; } = string.Empty;

    public List<ApplyChangeItem> Items { get; init; } = new();

    public DateTime AppliedUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Pure builder for <see cref="ApplyChangeSummary"/> — unit-testable without Win32.
/// Call with a profile whose Enable* flags reflect what actually succeeded.
/// </summary>
public static class ApplyChangeSummaryBuilder
{
    /// <summary>
    /// Build a change summary from profile core flags, applied catalog tweaks, and plugin ids.
    /// </summary>
    public static ApplyChangeSummary FromProfile(
        OptimizationProfile profile,
        IReadOnlyList<TweakDefinition>? appliedTweaks = null,
        IReadOnlyList<string>? pluginIds = null)
    {
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        var items = new List<ApplyChangeItem>();

        if (profile.EnableTimer)
        {
            items.Add(new ApplyChangeItem
            {
                Id = "timer",
                Area = "Timer",
                TitleKey = "changed.timer",
                Detail = $"Timer resolution target {profile.TimerTargetMs:0.##} ms held.",
                Risk = "safe",
                RequiresReboot = false
            });
        }

        if (profile.EnablePowerScheme)
        {
            string scheme = profile.UseUltimatePerformance
                ? "Ultimate Performance (or High Performance fallback)"
                : "High Performance";
            items.Add(new ApplyChangeItem
            {
                Id = "power",
                Area = "Power",
                TitleKey = "changed.power",
                Detail = $"Power scheme → {scheme}; processor min/max 100%; PCIe ASPM off.",
                Risk = "safe",
                RequiresReboot = false
            });
        }

        if (profile.EnableCoreParkingControl)
        {
            items.Add(new ApplyChangeItem
            {
                Id = "parking",
                Area = "Power",
                TitleKey = "changed.parking",
                Detail = $"Core parking mode: {profile.CoreParkingMode}.",
                Risk = "safe",
                RequiresReboot = false
            });
        }

        if (profile.EnableGameModeTweak)
        {
            items.Add(new ApplyChangeItem
            {
                Id = "gameMode",
                Area = "Game",
                TitleKey = "changed.gameMode",
                Detail = "Game Mode on; Game DVR disabled.",
                Risk = "safe",
                RequiresReboot = false
            });
        }

        if (profile.EnableHags)
        {
            items.Add(new ApplyChangeItem
            {
                Id = "hags",
                Area = "Gpu",
                TitleKey = "changed.hags",
                Detail = "Hardware-accelerated GPU scheduling (HAGS) enabled.",
                Risk = "moderate",
                RequiresReboot = true
            });
        }

        if (profile.EnableGpuLowLatency)
        {
            string prf = profile.MaxPreRenderedFrames > 0
                ? $" Max pre-rendered frames={profile.MaxPreRenderedFrames}."
                : string.Empty;
            items.Add(new ApplyChangeItem
            {
                Id = "gpuLowLatency",
                Area = "Gpu",
                TitleKey = "changed.gpuLowLatency",
                Detail = "GPU low-latency mode enabled." + prf,
                Risk = "moderate",
                RequiresReboot = false
            });
        }

        if (profile.EnableMemoryCleanup)
        {
            items.Add(new ApplyChangeItem
            {
                Id = "memory",
                Area = "Other",
                TitleKey = "changed.memory",
                Detail = "One-shot empty working sets for background processes (exclusions applied).",
                Risk = "safe",
                RequiresReboot = false
            });
        }

        if (appliedTweaks is { Count: > 0 })
        {
            foreach (var def in appliedTweaks)
            {
                if (def is null || string.IsNullOrWhiteSpace(def.Id))
                    continue;

                items.Add(new ApplyChangeItem
                {
                    Id = "tweak." + def.Id,
                    Area = MapCategoryToArea(def.CategoryId),
                    TitleKey = "changed.tweak." + def.Id,
                    Detail =
                        $"{def.Hive}\\{def.KeyPath}\\{def.ValueName} → {TweakValueCodec.Serialize(def.DesiredValue)}"
                        + (def.RequiresReboot ? " (reboot may be required)" : string.Empty),
                    Risk = MapRisk(def.Risk),
                    RequiresReboot = def.RequiresReboot
                });
            }
        }

        if (pluginIds is { Count: > 0 })
        {
            foreach (var pluginId in pluginIds)
            {
                if (string.IsNullOrWhiteSpace(pluginId))
                    continue;

                items.Add(new ApplyChangeItem
                {
                    Id = "plugin." + pluginId,
                    Area = "Plugin",
                    TitleKey = "changed.plugin." + pluginId,
                    Detail = $"Extension plugin applied: {pluginId}",
                    Risk = MapPluginRisk(pluginId),
                    RequiresReboot = false
                });
            }
        }

        return new ApplyChangeSummary
        {
            ProfileKey = OptimizationProfile.UiId(profile.Kind),
            ProfileKind = profile.Kind.ToString(),
            Items = items,
            AppliedUtc = DateTime.UtcNow
        };
    }

    /// <summary>Map catalog CategoryId → ApplyChangeItem.Area.</summary>
    public static string MapCategoryToArea(string? categoryId) => categoryId?.Trim().ToLowerInvariant() switch
    {
        "latency" => "Timer",
        "timer" => "Timer",
        "power" => "Power",
        "gpu" => "Gpu",
        "game" or "mmcss" => "Game",
        "network" => "Network",
        "input" => "Input",
        "service" or "services" => "Service",
        "cpu" => "Other",
        "plugin" => "Plugin",
        _ => "Other"
    };

    /// <summary>Map <see cref="TweakRisk"/> → safe|moderate|aggressive string.</summary>
    public static string MapRisk(TweakRisk risk) => risk switch
    {
        TweakRisk.Safe => "safe",
        TweakRisk.Moderate => "moderate",
        TweakRisk.Aggressive => "aggressive",
        TweakRisk.Advanced => "aggressive",
        _ => "moderate"
    };

    private static string MapPluginRisk(string pluginId)
    {
        // Experimental extensions are higher risk; hygiene/qos stay moderate/safe.
        if (pluginId.Contains("experimental", StringComparison.OrdinalIgnoreCase)
            || pluginId.Contains("msi", StringComparison.OrdinalIgnoreCase)
            || pluginId.Contains("interrupt", StringComparison.OrdinalIgnoreCase)
            || pluginId.Contains("blacklist", StringComparison.OrdinalIgnoreCase)
            || pluginId.Contains("service", StringComparison.OrdinalIgnoreCase))
            return "aggressive";

        if (pluginId.Contains("hygiene", StringComparison.OrdinalIgnoreCase)
            || pluginId.Contains("qos", StringComparison.OrdinalIgnoreCase)
            || pluginId.Contains("registry", StringComparison.OrdinalIgnoreCase))
            return "safe";

        return "moderate";
    }
}
