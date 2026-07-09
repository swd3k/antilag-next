using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// P/Invoke к psapi.dll — очистка рабочего набора памяти процесса (EmptyWorkingSet).
/// Используется для освобождения RAM фоновых процессов.
/// </summary>
internal static class Psapi
{
    private const string Lib = "psapi.dll";

    /// <summary>
    /// Уменьшить рабочий набор процесса до минимума. Эквивалент SetProcessWorkingSetSize с (-1,-1).
    /// Возвращает true при успехе. Требует PROCESS_SET_QUOTA.
    /// </summary>
    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);
}

/// <summary>
/// P/Invoke к kernel32.dll: открытие процессов для очистки памяти.
/// </summary>
internal static class ProcessNative
{
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_SET_QUOTA = 0x0100;
    public const uint PROCESS_VM_READ = 0x0010;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);
}
