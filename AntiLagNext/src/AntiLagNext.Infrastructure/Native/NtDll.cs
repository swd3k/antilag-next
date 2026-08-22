using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// P/Invoke to ntdll.dll — timer resolution and OS version.
/// NtQueryTimerResolution / NtSetTimerResolution are undocumented but stable.
/// Period is in 100-ns units (1 ms = 10 000).
///
/// Windows 11 22H2+: resolution is per-process unless
/// HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel\GlobalTimerResolutionRequests = 1
/// (reboot required). Pair with winmm timeBeginPeriod.
/// </summary>
internal static class NtDll
{
    private const string Lib = "ntdll.dll";

    /// <returns>NTSTATUS (0 = success).</returns>
    [DllImport(Lib, SetLastError = false)]
    public static extern uint NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    /// <returns>NTSTATUS (0 = success).</returns>
    [DllImport(Lib, SetLastError = false)]
    public static extern uint NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.Bool)] bool setResolution, out uint actualResolution);

    [DllImport(Lib, ExactSpelling = true)]
    public static extern int RtlGetVersion(ref OsVersionInfoEx versionInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OsVersionInfoEx
    {
        public uint dwOSVersionInfoSize;
        public uint dwMajorVersion;
        public uint dwMinorVersion;
        public uint dwBuildNumber;
        public uint dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    public static int QueryBuildNumber()
    {
        try
        {
            var info = new OsVersionInfoEx
            {
                dwOSVersionInfoSize = (uint)Marshal.SizeOf<OsVersionInfoEx>()
            };
            if (RtlGetVersion(ref info) == 0 && info.dwBuildNumber > 0)
                return (int)info.dwBuildNumber;
        }
        catch
        {
            /* fall through */
        }

        try
        {
            int build = Environment.OSVersion.Version.Build;
            if (build > 0) return build;
        }
        catch
        {
            /* ignore */
        }

        return 0;
    }
}
