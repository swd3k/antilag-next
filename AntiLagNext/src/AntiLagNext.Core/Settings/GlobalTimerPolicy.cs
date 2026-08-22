namespace AntiLagNext.Core.Settings;

/// <summary>
/// Windows 11 22H2+ made <c>NtSetTimerResolution</c> per-process.
/// Games only inherit the hold if <c>GlobalTimerResolutionRequests=1</c> (reboot)
/// plus a process that actually calls <c>timeBeginPeriod</c> / <c>NtSetTimerResolution</c>.
/// </summary>
public static class GlobalTimerPolicy
{
    /// <summary>Windows 11 first public build.</summary>
    public const int Windows11Build = 22000;

    /// <summary>22H2 — timer resolution became per-process.</summary>
    public const int PerProcessTimerBuild = 22621;

    public static bool IsWindows11(int build) => build >= Windows11Build;

    public static bool IsPerProcessTimerOs(int build) => build >= PerProcessTimerBuild;

    /// <summary>
    /// True when other processes (games) inherit a high-resolution timer without a reboot.
    /// Win10: always. Win11 22H2+: only if the kernel key was already 1 at process start.
    /// </summary>
    public static bool GamesInheritTimer(int build, bool globalRequestsEnabledAtProcessStart)
    {
        if (!IsPerProcessTimerOs(build))
            return true;
        return globalRequestsEnabledAtProcessStart;
    }

    /// <summary>
    /// Stable UI/CLI token: released | global | local | pendingReboot.
    /// </summary>
    public static string ScopeKey(int build, bool globalRequestsEnabledAtProcessStart, bool timerHeld)
    {
        if (!timerHeld)
            return "released";
        if (GamesInheritTimer(build, globalRequestsEnabledAtProcessStart))
            return "global";
        // Key missing/zero: games will not inherit until reboot after catalog writes the DWORD.
        return globalRequestsEnabledAtProcessStart ? "global" : "pendingReboot";
    }
}
