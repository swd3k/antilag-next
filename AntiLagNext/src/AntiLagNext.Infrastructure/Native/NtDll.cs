using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// P/Invoke к ntdll.dll — функции разрешения системного таймера.
/// NtQueryTimerResolution / NtSetTimerResolution — недокументированные, но стабильные API.
/// Разрешение измеряется в единицах по 100 нс (1 мс = 10 000).
///
/// Важно: начиная с Windows 11 22H2, глобальное разрешение таймера стало per-process —
/// приложение может повлиять только на свой собственный таймер и таймер процессов,
/// которым оно передаёт фокус. Это ограничение мы отражаем в UI.
/// </summary>
internal static class NtDll
{
    private const string Lib = "ntdll.dll";

    /// <summary>
    /// Запросить доступные границы разрешения таймера.
    /// </summary>
    /// <param name="minimumResolution">Минимальный период (самое высокое разрешение), 100 нс.</param>
    /// <param name="maximumResolution">Максимальный период (самое низкое разрешение), 100 нс.</param>
    /// <param name="currentResolution">Текущий период, 100 нс.</param>
    /// <returns>NTSTATUS (0 = успех).</returns>
    [DllImport(Lib, SetLastError = false)]
    public static extern uint NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    /// <summary>
    /// Установить разрешение таймера.
    /// </summary>
    /// <param name="desiredResolution">Желаемый период в 100 нс.</param>
    /// <param name="setResolution">true — установить; false — отпустить (вернуть к значению по умолчанию).</param>
    /// <param name="actualResolution">Фактически установленный период.</param>
    /// <returns>NTSTATUS (0 = успех).</returns>
    [DllImport(Lib, SetLastError = false)]
    public static extern uint NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.Bool)] bool setResolution, out uint actualResolution);
}
