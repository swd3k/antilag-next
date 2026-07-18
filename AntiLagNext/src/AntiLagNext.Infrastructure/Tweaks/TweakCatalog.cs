using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Plugins;

namespace AntiLagNext.Infrastructure.Tweaks;

/// <summary>
/// Static catalog of Winrift-inspired latency registry tweaks.
/// Gaming/Max get Safe+Moderate entries; Office gets a smaller subset.
/// Aggressive/Advanced are listed for future opt-in UI — never auto-applied by <see cref="ForProfile"/>.
/// </summary>
public static class TweakCatalog
{
    // RegistryValueKind.DWord = 4 (avoid Microsoft.Win32 in public surface of definitions)
    private const int DWord = TweakValueCodec.KindDWord;

    private static readonly ProfileKind[] GamingMax =
    {
        ProfileKind.Gaming,
        ProfileKind.MaxPerformance
    };

    private static readonly ProfileKind[] GamingMaxOffice =
    {
        ProfileKind.Gaming,
        ProfileKind.MaxPerformance,
        ProfileKind.Office
    };

    private static readonly TweakDefinition[] Items =
    {
        new()
        {
            Id = "latency.interrupt_steering",
            CategoryId = "latency",
            NameKey = "tweak.interrupt_steering.name",
            DescriptionKey = "tweak.interrupt_steering.desc",
            Risk = TweakRisk.Moderate,
            Impact = LatencyImpact.Medium,
            RequiresReboot = true,
            Hive = "HKLM",
            KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
            ValueName = "InterruptSteeringDisabled",
            ValueKind = DWord,
            DesiredValue = 1,
            Profiles = GamingMax
        },
        new()
        {
            Id = "latency.serialize_timer",
            CategoryId = "latency",
            NameKey = "tweak.serialize_timer.name",
            DescriptionKey = "tweak.serialize_timer.desc",
            Risk = TweakRisk.Moderate,
            Impact = LatencyImpact.Medium,
            RequiresReboot = true,
            Hive = "HKLM",
            KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
            ValueName = "SerializeTimerExpiration",
            ValueKind = DWord,
            DesiredValue = 1,
            Profiles = GamingMax
        },
        new()
        {
            Id = "cpu.win32_priority_separation",
            CategoryId = "cpu",
            NameKey = "tweak.win32_priority.name",
            DescriptionKey = "tweak.win32_priority.desc",
            Risk = TweakRisk.Moderate,
            Impact = LatencyImpact.Medium,
            RequiresReboot = false,
            Hive = "HKLM",
            KeyPath = @"SYSTEM\CurrentControlSet\Control\PriorityControl",
            ValueName = "Win32PrioritySeparation",
            ValueKind = DWord,
            DesiredValue = 0x24,
            Profiles = GamingMax
        },
        new()
        {
            Id = "network.throttling_index",
            CategoryId = "network",
            NameKey = "tweak.network_throttling.name",
            DescriptionKey = "tweak.network_throttling.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Low,
            RequiresReboot = false,
            Hive = "HKLM",
            KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            ValueName = "NetworkThrottlingIndex",
            ValueKind = DWord,
            // 0xFFFFFFFF — store as unsigned-friendly int (-1)
            DesiredValue = unchecked((int)0xFFFFFFFFu),
            Profiles = GamingMaxOffice
        },
        new()
        {
            Id = "network.no_lazy_mode",
            CategoryId = "network",
            NameKey = "tweak.no_lazy_mode.name",
            DescriptionKey = "tweak.no_lazy_mode.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Low,
            RequiresReboot = false,
            Hive = "HKLM",
            KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            ValueName = "NoLazyMode",
            ValueKind = DWord,
            DesiredValue = 1,
            Profiles = GamingMaxOffice
        },
        new()
        {
            Id = "input.mouse_queue",
            CategoryId = "input",
            NameKey = "tweak.mouse_queue.name",
            DescriptionKey = "tweak.mouse_queue.desc",
            Risk = TweakRisk.Moderate,
            Impact = LatencyImpact.Medium,
            RequiresReboot = true,
            Hive = "HKLM",
            KeyPath = @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
            ValueName = "MouseDataQueueSize",
            ValueKind = DWord,
            DesiredValue = 20,
            Profiles = GamingMax
        },
        new()
        {
            Id = "input.keyboard_queue",
            CategoryId = "input",
            NameKey = "tweak.keyboard_queue.name",
            DescriptionKey = "tweak.keyboard_queue.desc",
            Risk = TweakRisk.Moderate,
            Impact = LatencyImpact.Medium,
            RequiresReboot = true,
            Hive = "HKLM",
            KeyPath = @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters",
            ValueName = "KeyboardDataQueueSize",
            ValueKind = DWord,
            DesiredValue = 20,
            Profiles = GamingMax
        },
        new()
        {
            Id = "power.throttling_off",
            CategoryId = "power",
            NameKey = "tweak.power_throttling.name",
            DescriptionKey = "tweak.power_throttling.desc",
            Risk = TweakRisk.Moderate,
            Impact = LatencyImpact.Medium,
            RequiresReboot = false,
            Hive = "HKLM",
            KeyPath = @"SYSTEM\CurrentControlSet\Control\Power",
            ValueName = "PowerThrottlingOff",
            ValueKind = DWord,
            DesiredValue = 1,
            Profiles = GamingMax
        },
        new()
        {
            Id = "mmcss.lazy_mode_timeout",
            CategoryId = "mmcss",
            NameKey = "tweak.lazy_mode_timeout.name",
            DescriptionKey = "tweak.lazy_mode_timeout.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Low,
            RequiresReboot = false,
            Hive = "HKLM",
            KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            ValueName = "LazyModeTimeout",
            ValueKind = DWord,
            DesiredValue = 25000,
            Profiles = GamingMaxOffice
        },

        // ── Phase 3: network Nagle hygiene ──
        new()
        {
            Id = "network.tcp_ack_frequency",
            CategoryId = "network",
            NameKey = "tweak.tcp_ack.name",
            DescriptionKey = "tweak.tcp_ack.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Low,
            RequiresReboot = false,
            Hive = "HKLM",
            KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            ValueName = "TcpAckFrequency",
            ValueKind = DWord,
            DesiredValue = 1,
            Profiles = GamingMax
        },
        new()
        {
            Id = "network.tcp_no_delay",
            CategoryId = "network",
            NameKey = "tweak.tcp_nodelay.name",
            DescriptionKey = "tweak.tcp_nodelay.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Low,
            RequiresReboot = false,
            Hive = "HKLM",
            KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            ValueName = "TCPNoDelay",
            ValueKind = DWord,
            DesiredValue = 1,
            Profiles = GamingMax
        },

        // ── Phase 3: input accessibility (filter/sticky noise) ──
        new()
        {
            Id = "input.sticky_keys_flags",
            CategoryId = "input",
            NameKey = "tweak.sticky_keys.name",
            DescriptionKey = "tweak.sticky_keys.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Low,
            RequiresReboot = false,
            Hive = "HKCU",
            KeyPath = @"Control Panel\Accessibility\StickyKeys",
            ValueName = "Flags",
            ValueKind = DWord,
            DesiredValue = 506,
            Profiles = GamingMax
        },
        new()
        {
            Id = "input.toggle_keys_flags",
            CategoryId = "input",
            NameKey = "tweak.toggle_keys.name",
            DescriptionKey = "tweak.toggle_keys.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Low,
            RequiresReboot = false,
            Hive = "HKCU",
            KeyPath = @"Control Panel\Accessibility\ToggleKeys",
            ValueName = "Flags",
            ValueKind = DWord,
            DesiredValue = 58,
            Profiles = GamingMax
        },
        new()
        {
            Id = "input.keyboard_response_delay",
            CategoryId = "input",
            NameKey = "tweak.kbd_delay.name",
            DescriptionKey = "tweak.kbd_delay.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Low,
            RequiresReboot = false,
            Hive = "HKCU",
            KeyPath = @"Control Panel\Accessibility\Keyboard Response",
            ValueName = "DelayBeforeAcceptance",
            ValueKind = DWord,
            DesiredValue = 0,
            Profiles = GamingMax
        },
        // Mouse accel off — Windows stores these as REG_SZ
        new()
        {
            Id = "input.mouse_accel_off",
            CategoryId = "input",
            NameKey = "tweak.mouse_accel.name",
            DescriptionKey = "tweak.mouse_accel.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Medium,
            RequiresReboot = false,
            Hive = "HKCU",
            KeyPath = @"Control Panel\Mouse",
            ValueName = "MouseSpeed",
            ValueKind = TweakValueCodec.KindString,
            DesiredValue = "0",
            Profiles = GamingMax
        },
        new()
        {
            Id = "input.mouse_threshold1",
            CategoryId = "input",
            NameKey = "tweak.mouse_th1.name",
            DescriptionKey = "tweak.mouse_th1.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Medium,
            RequiresReboot = false,
            Hive = "HKCU",
            KeyPath = @"Control Panel\Mouse",
            ValueName = "MouseThreshold1",
            ValueKind = TweakValueCodec.KindString,
            DesiredValue = "0",
            Profiles = GamingMax
        },
        new()
        {
            Id = "input.mouse_threshold2",
            CategoryId = "input",
            NameKey = "tweak.mouse_th2.name",
            DescriptionKey = "tweak.mouse_th2.desc",
            Risk = TweakRisk.Safe,
            Impact = LatencyImpact.Medium,
            RequiresReboot = false,
            Hive = "HKCU",
            KeyPath = @"Control Panel\Mouse",
            ValueName = "MouseThreshold2",
            ValueKind = TweakValueCodec.KindString,
            DesiredValue = "0",
            Profiles = GamingMax
        },
    };

    /// <summary>Full catalog (including any future Aggressive/Advanced entries).</summary>
    public static IReadOnlyList<TweakDefinition> All => Items;

    public static TweakDefinition? GetById(string id) =>
        Items.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Tweaks auto-applied for a profile: tagged for that kind and risk Safe or Moderate only.
    /// Default/Custom → empty (no silent Aggressive path).
    /// </summary>
    public static IReadOnlyList<TweakDefinition> ForProfile(ProfileKind kind)
    {
        if (kind is ProfileKind.Default or ProfileKind.Custom)
            return Array.Empty<TweakDefinition>();

        return Items
            .Where(t =>
                t.Risk is TweakRisk.Safe or TweakRisk.Moderate
                && t.Profiles.Contains(kind))
            .ToList();
    }
}
