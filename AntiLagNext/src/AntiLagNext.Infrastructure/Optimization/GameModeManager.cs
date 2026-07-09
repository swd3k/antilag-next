using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using Microsoft.Win32;

namespace AntiLagNext.Infrastructure.Optimization;

/// <summary>
/// Твики Windows Game Mode / Game Bar / Game DVR / HAGS через реестр.
/// Перед изменением вызывающий код должен сделать Snapshot через IBackupService.
/// </summary>
public sealed class GameModeManager : IGameModeManager
{
    private readonly IBackupService? _backup;
    private Guid? _activeSessionId;

    public GameModeManager(IBackupService backup) => _backup = backup;

    /// <summary>Привязать текущую сессию бэкапа (вызывается из ProfileService).</summary>
    public void BindBackupSession(Guid sessionId) => _activeSessionId = sessionId;

    public OperationResult SetGameMode(bool enabled, bool disableGameDvr)
    {
        try
        {
            // Game Mode: HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled = 1/0
            SnapshotAndSet(
                Registry.CurrentUser,
                PowerGuids.GameBarKey,
                "AutoGameModeEnabled",
                enabled ? 1 : 0);

            SnapshotAndSet(
                Registry.CurrentUser,
                PowerGuids.GameBarKey,
                "AllowAutoGameMode",
                enabled ? 1 : 0);

            // Game DVR / Game Bar capture (часто источник micro-stutter)
            if (disableGameDvr)
            {
                SnapshotAndSet(
                    Registry.CurrentUser,
                    PowerGuids.GameDvrKey,
                    "AppCaptureEnabled",
                    0);

                SnapshotAndSet(
                    Registry.LocalMachine,
                    PowerGuids.GameConfigStoreKey,
                    "GameDVR_Enabled",
                    0);
            }
            else if (enabled)
            {
                // Не навязываем DVR, если пользователь его хочет
            }

            return OperationResult.Ok(
                enabled
                    ? "Game Mode включён" + (disableGameDvr ? ", Game DVR отключён." : ".")
                    : "Game Mode выключен.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail("Нет прав на запись реестра (нужен администратор).", detail: ex.Message, ex: ex);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Не удалось изменить Game Mode.", detail: ex.Message, ex: ex);
        }
    }

    public OperationResult SetHags(bool enabled)
    {
        try
        {
            // HAGS: HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode
            // 1 = Off, 2 = On (Windows 10 2004+)
            SnapshotAndSet(
                Registry.LocalMachine,
                PowerGuids.GraphicsDriversKey,
                "HwSchMode",
                enabled ? 2 : 1);

            return OperationResult.Ok(
                enabled
                    ? "HAGS включён (может потребоваться перезагрузка)."
                    : "HAGS выключен (может потребоваться перезагрузка).");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail("Нет прав на HAGS (нужен администратор / HKLM).", detail: ex.Message, ex: ex);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Не удалось изменить HAGS.", detail: ex.Message, ex: ex);
        }
    }

    private void SnapshotAndSet(RegistryKey root, string keyPath, string valueName, int value)
    {
        if (_backup != null && _activeSessionId is Guid sid)
        {
            // SnapshotCurrentRegistryValue есть только на concrete BackupService —
            // делаем вручную через SnapshotRegistryValue
            using var key = root.OpenSubKey(keyPath, writable: false);
            var existing = key?.GetValue(valueName);
            var kind = existing == null ? RegistryValueKind.DWord : key!.GetValueKind(valueName);

            string hive = root.Name switch
            {
                "HKEY_LOCAL_MACHINE" => "HKLM",
                "HKEY_CURRENT_USER" => "HKCU",
                _ => "HKLM"
            };

            _backup.SnapshotRegistryValue(sid, new RegistryBackupEntry
            {
                Hive = hive,
                KeyPath = keyPath,
                ValueName = valueName,
                ValueKind = (int)kind,
                SerializedValue = existing?.ToString(),
                WasMissing = existing == null
            });
        }

        using var writeKey = root.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException($"Не удалось открыть ключ {keyPath}");
        writeKey.SetValue(valueName, value, RegistryValueKind.DWord);
    }
}
