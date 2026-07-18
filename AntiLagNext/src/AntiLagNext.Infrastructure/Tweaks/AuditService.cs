using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Storage;

namespace AntiLagNext.Infrastructure.Tweaks;

/// <summary>
/// Read-only audit of known latency-related settings (recommendations for UI).
/// </summary>
public sealed class AuditService : IAuditService
{
    public IReadOnlyList<AuditFinding> Scan()
    {
        var findings = new List<AuditFinding>();

        // HAGS HwSchMode: 2 = On
        CheckDword(
            findings,
            id: "audit.hags",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
            name: "HwSchMode",
            preferred: "2",
            titleKey: "audit.hags.title",
            badDetail: "HAGS (HwSchMode) is off or missing — hardware GPU scheduling may help frame pacing.",
            severity: "info",
            suggestedTweakId: null,
            canFix: false);

        // Network throttling
        CheckDword(
            findings,
            id: "audit.network_throttling",
            hive: "HKLM",
            path: @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            name: "NetworkThrottlingIndex",
            preferred: "4294967295",
            titleKey: "audit.network_throttling.title",
            badDetail: "NetworkThrottlingIndex is not fully disabled (expected 0xFFFFFFFF).",
            severity: "warn",
            suggestedTweakId: "network.throttling_index",
            canFix: true);

        CheckDword(
            findings,
            id: "audit.interrupt_steering",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
            name: "InterruptSteeringDisabled",
            preferred: "1",
            titleKey: "audit.interrupt_steering.title",
            badDetail: "InterruptSteeringDisabled is not set to 1.",
            severity: "info",
            suggestedTweakId: "latency.interrupt_steering",
            canFix: true);

        CheckDword(
            findings,
            id: "audit.serialize_timer",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
            name: "SerializeTimerExpiration",
            preferred: "1",
            titleKey: "audit.serialize_timer.title",
            badDetail: "SerializeTimerExpiration is not set to 1.",
            severity: "info",
            suggestedTweakId: "latency.serialize_timer",
            canFix: true);

        CheckDword(
            findings,
            id: "audit.power_throttling",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Control\Power",
            name: "PowerThrottlingOff",
            preferred: "1",
            titleKey: "audit.power_throttling.title",
            badDetail: "PowerThrottlingOff is not set — Windows may throttle background/power-saving tasks aggressively.",
            severity: "warn",
            suggestedTweakId: "power.throttling_off",
            canFix: true);

        CheckDword(
            findings,
            id: "audit.win32_priority",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Control\PriorityControl",
            name: "Win32PrioritySeparation",
            preferred: "36",
            titleKey: "audit.win32_priority.title",
            badDetail: "Win32PrioritySeparation is not 0x24 (short, fixed, high foreground boost).",
            severity: "info",
            suggestedTweakId: "cpu.win32_priority_separation",
            canFix: true);

        // Input queues — only report if key/value present (driver parameters may be absent)
        CheckDwordIfPresent(
            findings,
            id: "audit.mouse_queue",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
            name: "MouseDataQueueSize",
            preferred: "20",
            titleKey: "audit.mouse_queue.title",
            badDetail: "MouseDataQueueSize is present but not 20 (lower reduces buffer latency).",
            severity: "info",
            suggestedTweakId: "input.mouse_queue");

        CheckDwordIfPresent(
            findings,
            id: "audit.keyboard_queue",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters",
            name: "KeyboardDataQueueSize",
            preferred: "20",
            titleKey: "audit.keyboard_queue.title",
            badDetail: "KeyboardDataQueueSize is present but not 20.",
            severity: "info",
            suggestedTweakId: "input.keyboard_queue");

        CheckDword(
            findings,
            id: "audit.tcp_ack",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            name: "TcpAckFrequency",
            preferred: "1",
            titleKey: "audit.tcp_ack.title",
            badDetail: "TcpAckFrequency is not 1 (Nagle-related ACK delay may be higher).",
            severity: "info",
            suggestedTweakId: "network.tcp_ack_frequency",
            canFix: true);

        CheckDword(
            findings,
            id: "audit.tcp_nodelay",
            hive: "HKLM",
            path: @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            name: "TCPNoDelay",
            preferred: "1",
            titleKey: "audit.tcp_nodelay.title",
            badDetail: "TCPNoDelay is not 1.",
            severity: "info",
            suggestedTweakId: "network.tcp_no_delay",
            canFix: true);

        // Active optimization flag
        var active = ActiveStateStore.Load();
        if (!active.Active)
        {
            findings.Add(new AuditFinding
            {
                Id = "audit.active_state",
                Severity = "info",
                TitleKey = "audit.active_state.title",
                Detail = "AntiLag Next optimizations are not marked active (ActiveStateStore).",
                SuggestedTweakId = null,
                CanFix = false,
                Area = MapArea("audit.active_state")
            });
        }
        else
        {
            findings.Add(new AuditFinding
            {
                Id = "audit.active_state",
                Severity = "info",
                TitleKey = "audit.active_state.title",
                Detail = $"Optimizations active (profile={active.ProfileName ?? "?"}, since {active.AppliedUtc:u}).",
                SuggestedTweakId = null,
                CanFix = false,
                Area = MapArea("audit.active_state")
            });
        }

        return findings;
    }

    /// <summary>
    /// Map finding id (and optional suggested tweak / path) → Health area.
    /// Areas: Gpu|Network|Timer|Power|Input|System|Other
    /// </summary>
    public static string MapArea(string id, string? suggestedTweakId = null, string? path = null)
    {
        string key = (id ?? string.Empty).ToLowerInvariant();
        string tweak = (suggestedTweakId ?? string.Empty).ToLowerInvariant();
        string p = (path ?? string.Empty).ToLowerInvariant();

        if (key.Contains("hags") || key.Contains("gpu") || p.Contains("graphicsdrivers"))
            return "Gpu";
        if (key.Contains("network") || key.Contains("tcp") || tweak.StartsWith("network.")
            || p.Contains("tcpip") || p.Contains("multimedia\\systemprofile"))
            return "Network";
        if (key.Contains("timer") || key.Contains("serialize_timer") || key.Contains("interrupt_steering")
            || tweak.StartsWith("latency.") || p.Contains("session manager\\kernel"))
            return "Timer";
        if (key.Contains("power") || tweak.StartsWith("power.") || p.Contains("control\\power"))
            return "Power";
        if (key.Contains("mouse") || key.Contains("keyboard") || key.Contains("input")
            || tweak.StartsWith("input.") || p.Contains("mouclass") || p.Contains("kbdclass"))
            return "Input";
        if (key.Contains("win32_priority") || key.Contains("active_state") || key.Contains("cpu")
            || tweak.StartsWith("cpu.") || p.Contains("prioritycontrol"))
            return "System";

        return "Other";
    }

    private static void CheckDword(
        List<AuditFinding> findings,
        string id,
        string hive,
        string path,
        string name,
        string preferred,
        string titleKey,
        string badDetail,
        string severity,
        string? suggestedTweakId,
        bool canFix)
    {
        string? current = ReadNormalized(hive, path, name);
        bool ok = current != null && TweakValueCodec.ValuesEqual(preferred, current, "DWord");
        if (ok) return;

        findings.Add(new AuditFinding
        {
            Id = id,
            Severity = severity,
            TitleKey = titleKey,
            Detail = current is null
                ? badDetail + " (value missing)"
                : $"{badDetail} Current={current}, expected={preferred}.",
            SuggestedTweakId = suggestedTweakId,
            CanFix = canFix && suggestedTweakId != null,
            Area = MapArea(id, suggestedTweakId, path)
        });
    }

    private static void CheckDwordIfPresent(
        List<AuditFinding> findings,
        string id,
        string hive,
        string path,
        string name,
        string preferred,
        string titleKey,
        string badDetail,
        string severity,
        string? suggestedTweakId)
    {
        string? current = ReadNormalized(hive, path, name);
        if (current is null) return; // not present — skip

        if (TweakValueCodec.ValuesEqual(preferred, current, "DWord"))
            return;

        findings.Add(new AuditFinding
        {
            Id = id,
            Severity = severity,
            TitleKey = titleKey,
            Detail = $"{badDetail} Current={current}, expected={preferred}.",
            SuggestedTweakId = suggestedTweakId,
            CanFix = suggestedTweakId != null,
            Area = MapArea(id, suggestedTweakId, path)
        });
    }

    private static string? ReadNormalized(string hive, string path, string name)
    {
        try
        {
            var root = RegistryTweakEngine.ResolveHive(hive);
            if (root is null) return null;
            using var key = root.OpenSubKey(path, writable: false);
            return TweakValueCodec.NormalizeLive(key?.GetValue(name));
        }
        catch
        {
            return null;
        }
    }
}
