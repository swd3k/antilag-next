namespace AntiLagNext.Core.Settings;

/// <summary>
/// Rules for re-applying optimizations when the app starts.
/// First interactive launch never auto-applies — only after the user has enabled
/// optimization themselves and left it ON (ActiveState), with AutoApplyOnStartup
/// (or Windows logon via --autostart).
/// </summary>
public static class AutoApplyPolicy
{
    /// <summary>
    /// Whether startup should call Apply again.
    /// </summary>
    /// <param name="userEnabledOptimization">
    /// User has successfully enabled optimization at least once (lifecycle flag).
    /// </param>
    /// <param name="autoApplyOnStartup">
    /// Settings toggle "Optimize on startup" (also set true after first Enable).
    /// </param>
    /// <param name="optimizationLeftActive">
    /// Last session left optimization ON (<c>ActiveStateStore.Active</c>).
    /// After Reset / Disable this is false — must not re-apply.
    /// </param>
    /// <param name="autostartMode">
    /// Process started with <c>--autostart</c> (Task Scheduler logon).
    /// Still requires user opt-in + left-active; does not bypass Disable.
    /// </param>
    public static bool ShouldAutoApplyOnStart(
        bool userEnabledOptimization,
        bool autoApplyOnStartup,
        bool optimizationLeftActive,
        bool autostartMode)
    {
        // Never on a cold first-run profile
        if (!userEnabledOptimization)
            return false;

        // User explicitly turned optimization OFF last time (or never successfully applied)
        if (!optimizationLeftActive)
            return false;

        // Preference: settings checkbox, or logon helper (same preference surface)
        return autoApplyOnStartup || autostartMode;
    }

    /// <summary>
    /// Human-readable skip reason for logs (null if should apply).
    /// </summary>
    public static string? DescribeSkipReason(
        bool userEnabledOptimization,
        bool autoApplyOnStartup,
        bool optimizationLeftActive,
        bool autostartMode,
        bool english)
    {
        if (ShouldAutoApplyOnStart(
                userEnabledOptimization, autoApplyOnStartup, optimizationLeftActive, autostartMode))
            return null;

        if (!userEnabledOptimization)
            return english
                ? "Auto-optimize skipped: user has not enabled optimization yet (first use is manual)."
                : "Авто-оптимизация пропущена: пользователь ещё не включал оптимизацию (первый раз — вручную).";

        if (!optimizationLeftActive)
            return english
                ? "Auto-optimize skipped: optimization was left off (Reset / Disable)."
                : "Авто-оптимизация пропущена: оптимизация была выключена (Сброс / Отключить).";

        if (!autoApplyOnStartup && !autostartMode)
            return english
                ? "Auto-optimize skipped: “Optimize on startup” is off in Settings."
                : "Авто-оптимизация пропущена: «Оптимизация при старте» выключена в Настройках.";

        return english
            ? "Auto-optimize skipped."
            : "Авто-оптимизация пропущена.";
    }
}
