using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// P/Invoke к kernel32.dll: таймеры высокого разрешения, топология процессора, память.
/// </summary>
internal static class Kernel32
{
    private const string Lib = "kernel32.dll";

    // --- Высокоточный таймер (QueryPerformanceCounter/Frequency) ---

    [DllImport(Lib)]
    public static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

    [DllImport(Lib)]
    public static extern bool QueryPerformanceFrequency(out long lpFrequency);

    // --- Waitable timer (для зонда DPC/scheduling-задержки в MonitoringService) ---

    /// <summary>Создать waitable timer. EVENT_MODIFY_STATE = 0x0002.</summary>
    [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWaitableTimer(IntPtr lpTimerAttributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, string? lpTimerName);

    /// <summary>Установить период waitable timer (в 100-нс; отрицательное = относительное).</summary>
    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWaitableTimer(IntPtr hTimer, ref long dueTime, int period, TimerCompletionDelegate? completionRoutine, IntPtr argToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool resume);

    public delegate void TimerCompletionDelegate(IntPtr lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.U4)] uint dwTimerLowValue, [MarshalAs(UnmanagedType.U4)] uint dwTimerHighValue);

    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CancelWaitableTimer(IntPtr hTimer);

    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    /// <summary>WAIT_OBJECT_0 и т.д.</summary>
    [DllImport(Lib, SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    public const uint WaitObject0 = 0x00000000;
    public const uint WaitTimeout = 0x00000102;
    public const uint Infinite = 0xFFFFFFFF;

    // --- System-wide CPU: GetSystemTimes ---

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    /// <summary>
    /// Idle / Kernel / User times системы (100-нс). Kernel включает idle.
    /// busy = (Kernel - Idle) + User; total = Kernel + User.
    /// </summary>
    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    // --- Топология процессора: GetLogicalProcessorInformationEx ---

    /// <summary>Тип отношения логического процессора.</summary>
    public const int RelationProcessorCore = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
    {
        public int Relationship;        // RelationProcessorCore и т.д.
        public int Size;                // Размер записи в байтах
        // Дальше идёт union; для RelationProcessorCore — PROCESSOR_RELATIONSHIP.
        // Мы читаем вручную через смещения, т.к. layout переменный.
    }

    /// <summary>
    /// Получить расширенную информацию о логических процессорах.
    /// Буфер переменного размера — caller выделяет дважды (query, then alloc).
    /// </summary>
    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);
}
