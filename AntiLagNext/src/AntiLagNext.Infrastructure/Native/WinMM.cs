using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// Multimedia timer period. Documented companion to NtSetTimerResolution:
/// <c>timeBeginPeriod</c> is what Windows 11 22H2+ actually honors for a process,
/// and with <c>GlobalTimerResolutionRequests</c> it becomes system-wide again.
/// </summary>
internal static class WinMM
{
    private const string Lib = "winmm.dll";

    /// <returns>0 = TIMERR_NOERROR.</returns>
    [DllImport(Lib)]
    public static extern uint timeBeginPeriod(uint uPeriod);

    /// <returns>0 = TIMERR_NOERROR. Must pair with the same period passed to timeBeginPeriod.</returns>
    [DllImport(Lib)]
    public static extern uint timeEndPeriod(uint uPeriod);
}
