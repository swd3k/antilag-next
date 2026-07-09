using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// P/Invoke к powrprof.dll — управление схемами электропитания и их настройками.
/// Правильный Win32-путь (вместо шелла powercfg.exe, который использовал оригинальный AntiLag).
/// </summary>
internal static class PowrProf
{
    private const string Lib = "powrprof.dll";

    /// <summary>
    /// Получить GUID активной схемы. Caller освобождает память через LocalFree.
    /// </summary>
    [DllImport(Lib, ExactSpelling = true)]
    public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>Активировать схему по GUID (ref).</summary>
    [DllImport(Lib, ExactSpelling = true)]
    public static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    // --- Чтение значений (через указатели на GUID) ---

    [DllImport(Lib, ExactSpelling = true)]
    public static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        out uint acValueIndex);

    [DllImport(Lib, ExactSpelling = true)]
    public static extern uint PowerReadDCValueIndex(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        out uint dcValueIndex);

    // --- Запись значений (через ref Guid — удобнее в managed-коде) ---

    [DllImport(Lib, ExactSpelling = true)]
    public static extern uint PowerWriteACValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        uint acValueIndex);

    [DllImport(Lib, ExactSpelling = true)]
    public static extern uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        uint dcValueIndex);

    // Примечание: в powrprof.dll НЕТ PowerApplySetting. После PowerWrite* нужно
    // повторно вызвать PowerSetActiveScheme для немедленного применения значений.

    // GetSystemPowerStatus — в kernel32, не в powrprof
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 0 = батарея, 1 = сеть, 255 = неизвестно
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
