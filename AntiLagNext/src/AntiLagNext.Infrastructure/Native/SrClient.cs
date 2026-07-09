using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// P/Invoke к srclient.dll — создание точек восстановления системы (System Restore).
/// Требует прав администратора и включённой службы System Restore.
/// </summary>
internal static class SrClient
{
    private const string Lib = "srclient.dll";

    // EventType: BEGIN_SYSTEM_CHANGE / END_SYSTEM_CHANGE
    public const int BeginSystemChange = 100;
    public const int EndSystemChange = 101;

    // RestorePointType: MODIFY_SETTINGS (для настроек системы), APPLICATION_INSTALL и т.д.
    public const int ModifySettings = 100;
    public const int ApplicationInstall = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RESTOREPOINTINFO
    {
        public int dwEventType;          // BEGIN_SYSTEM_CHANGE / END_SYSTEM_CHANGE
        public int dwRestorePtType;      // MODIFY_SETTINGS / APPLICATION_INSTALL
        public long llSequenceNumber;    // 0 при BEGIN, значение из STATEMGRSTATUS при END
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;     // Описание точки (макс 256 символов, обрезается)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STATEMGRSTATUS
    {
        public uint nStatus;             // Код результата
        public long llSequenceNumber;    // Sequence number — нужен для END_SYSTEM_CHANGE
    }

    /// <summary>
    /// Создать/завершить точку восстановления.
    /// На Windows 10 1809+ действует квота: не более одной точки в 24 ч по умолчанию
    /// (реестр HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore\SystemRestorePointCreationFrequency, секунды).
    /// Мы выдерживаем задержку и обрабатываем код nStatus.
    /// </summary>
    [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SRSetRestorePointW(ref RESTOREPOINTINFO restorePtInfo, out STATEMGRSTATUS smStatus);
}
