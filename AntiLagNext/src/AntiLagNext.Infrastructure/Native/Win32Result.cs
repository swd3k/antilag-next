using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// Вспомогательные методы для обработки результатов Win32: преобразование кодов ошибок,
/// проверка NTSTATUS, формирование понятных сообщений.
/// Все P/Invoke-обёртки должны возвращать OperationResult с понятным сообщением — не сырой код.
/// </summary>
internal static class Win32Result
{
    /// <summary>
    /// Выбросить понятное исключение, если последнее Win32-выражение завершилось ошибкой.
    /// </summary>
    public static void ThrowIfLastError([System.Runtime.CompilerServices.CallerMemberName] string member = "")
    {
        int err = Marshal.GetLastWin32Error();
        if (err != 0)
        {
            throw new Win32Exception(err, $"Win32 ошибка в {member}: код {err}");
        }
    }

    /// <summary>
    /// Безопасно получить сообщение об ошибке по коду Win32.
    /// </summary>
    public static string FormatMessage(int errorCode)
    {
        try
        {
            return new Win32Exception(errorCode).Message;
        }
        catch
        {
            return $"Win32 код ошибки {errorCode}";
        }
    }
}

/// <summary>
/// Константы NTSTATUS для NtSetTimerResolution и др.
/// </summary>
internal static class NtStatus
{
    public const uint Success = 0x00000000;
    public const uint InvalidParameter = 0xC000000D;

    public static bool IsSuccess(uint status) => status == Success;
}
