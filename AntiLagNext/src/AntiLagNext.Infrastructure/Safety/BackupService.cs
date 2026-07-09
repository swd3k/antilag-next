using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Native;
using AntiLagNext.Infrastructure.Storage;
using Microsoft.Win32;

namespace AntiLagNext.Infrastructure.Safety;

/// <summary>
/// Реализация <see cref="IBackupService"/>: делает снимки значений реестра и power-plan
/// ДО изменения, сохраняет их в JSON и умеет восстанавливать.
///
/// Архитектура сессий:
/// - <see cref="BeginSession"/> создаёт запись в памяти, возвращает GUID.
/// - Сервисы оптимизации перед изменением вызывают SnapshotRegistryValue/SnapshotPowerValue.
/// - <see cref="CommitSession"/> сохраняет запись в JSON на диск (атомарно).
/// - <see cref="RestoreAsync"/> восстанавливает значения.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly Dictionary<Guid, BackupRecord> _sessions = new();
    private readonly object _lock = new();

    public string BackupDirectory => AppPaths.BackupDirectory;

    public Guid BeginSession(string operationName, string? activeSchemeGuidBefore)
    {
        var id = Guid.NewGuid();
        var record = new BackupRecord
        {
            OperationName = operationName,
            ActiveSchemeGuidBefore = activeSchemeGuidBefore
        };
        lock (_lock)
        {
            _sessions[id] = record;
        }
        return id;
    }

    public void SnapshotRegistryValue(Guid sessionId, RegistryBackupEntry entry)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var record))
                record.RegistryEntries.Add(entry);
        }
    }

    /// <summary>
    /// Сделать снимок ТЕКУЩЕГО значения реестра перед его изменением.
    /// Удобная обёртка: сервисы оптимизации вызывают этот метод, а BackupService сам читает
    /// текущее значение, определяет ValueKind и флаг WasMissing.
    /// </summary>
    public void SnapshotCurrentRegistryValue(Guid sessionId, RegistryKey root, string keyPath, string valueName)
    {
        using var key = root.OpenSubKey(keyPath, writable: false);
        var value = key?.GetValue(valueName);
        var kind = key?.GetValueKind(valueName) ?? RegistryValueKind.Unknown;

        var entry = new RegistryBackupEntry
        {
            Hive = HiveToString(root),
            KeyPath = keyPath,
            ValueName = valueName,
            ValueKind = (int)(value == null ? RegistryValueKind.Unknown : kind),
            SerializedValue = SerializeRegistryValue(value, kind),
            WasMissing = value == null
        };
        SnapshotRegistryValue(sessionId, entry);
    }

    private static string HiveToString(RegistryKey root) => root.Name switch
    {
        "HKEY_LOCAL_MACHINE" => "HKLM",
        "HKEY_CURRENT_USER" => "HKCU",
        "HKEY_CLASSES_ROOT" => "HKCR",
        "HKEY_USERS" => "HKU",
        "HKEY_CURRENT_CONFIG" => "HKCC",
        _ => "HKLM"
    };

    private static string? SerializeRegistryValue(object? value, RegistryValueKind kind) => value switch
    {
        null => null,
        int i => i.ToString(),
        long l => l.ToString(),
        string s => s,
        string[] arr => string.Join('\0', arr),
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value.ToString()
    };

    public void SnapshotPowerValue(Guid sessionId, PowerBackupEntry entry)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var record))
                record.PowerEntries.Add(entry);
        }
    }

    public void SetRestorePointStatus(Guid sessionId, bool created, string? error)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var record))
            {
                record.SystemRestorePointCreated = created;
                record.SystemRestorePointError = error;
            }
        }
    }

    public OperationResult<BackupRecord> CommitSession(Guid sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var record))
                return OperationResult<BackupRecord>.Fail("Сессия бэкапа не найдена.", detail: sessionId.ToString());

            try
            {
                Directory.CreateDirectory(BackupDirectory);
                string fileName = $"backup_{record.CreatedUtc:yyyyMMdd_HHmmss}_{sessionId.ToString().Substring(0, 8)}.json";
                string path = Path.Combine(BackupDirectory, fileName);
                JsonStorage.Save(path, record);
                _sessions.Remove(sessionId);
                PruneOldBackups();
                return OperationResult<BackupRecord>.Ok(record, $"Бэкап сохранён: {fileName}");
            }
            catch (Exception ex)
            {
                return OperationResult<BackupRecord>.Fail("Не удалось сохранить бэкап.", detail: ex.Message, ex: ex);
            }
        }
    }

    public OperationResult<BackupRecord> LoadLatest()
    {
        try
        {
            if (!Directory.Exists(BackupDirectory))
                return OperationResult<BackupRecord>.Fail("Бэкапов пока нет.", detail: BackupDirectory);

            var latest = Directory.GetFiles(BackupDirectory, "backup_*.json")
                .OrderByDescending(File.GetCreationTimeUtc)
                .FirstOrDefault();

            if (latest == null)
                return OperationResult<BackupRecord>.Fail("Бэкапов пока нет.");

            var record = JsonStorage.Load<BackupRecord>(latest);
            if (record == null)
                return OperationResult<BackupRecord>.Fail("Бэкап повреждён.", detail: latest);

            return OperationResult<BackupRecord>.Ok(record);
        }
        catch (Exception ex)
        {
            return OperationResult<BackupRecord>.Fail("Ошибка чтения бэкапа.", detail: ex.Message, ex: ex);
        }
    }

    public IReadOnlyList<BackupRecord> LoadAll()
    {
        var list = new List<BackupRecord>();
        if (!Directory.Exists(BackupDirectory)) return list;
        foreach (var file in Directory.GetFiles(BackupDirectory, "backup_*.json").OrderByDescending(File.GetCreationTimeUtc))
        {
            var r = JsonStorage.Load<BackupRecord>(file);
            if (r == null) continue;
            r.SourceFilePath = file;
            list.Add(r);
        }
        return list;
    }

    public OperationResult Delete(BackupRecord record)
    {
        try
        {
            if (record?.SourceFilePath is not { Length: > 0 } path || !File.Exists(path))
                return OperationResult.Fail("Файл бэкапа не найден.");
            File.Delete(path);
            return OperationResult.Ok("Бэкап удалён.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Не удалось удалить бэкап.", detail: ex.Message, ex: ex);
        }
    }

    public async Task<OperationResult> RestoreAsync(BackupRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) return OperationResult.Fail("Пустая запись бэкапа.");

        int restoredRegistry = 0, restoredPower = 0;
        var errors = new List<string>();

        // --- Восстановление реестра ---
        foreach (var entry in record.RegistryEntries)
        {
            try
            {
                RestoreRegistryEntry(entry);
                restoredRegistry++;
            }
            catch (Exception ex)
            {
                errors.Add($"Реестр {entry.Hive}\\{entry.KeyPath}\\{entry.ValueName}: {ex.Message}");
            }
        }

        // --- Восстановление power-plan ---
        foreach (var entry in record.PowerEntries)
        {
            try
            {
                RestorePowerEntry(entry);
                restoredPower++;
            }
            catch (Exception ex)
            {
                errors.Add($"Power {entry.SchemeGuid}/{entry.SettingGuid}: {ex.Message}");
            }
        }

        // --- Восстановление активной схемы ---
        if (!string.IsNullOrWhiteSpace(record.ActiveSchemeGuidBefore)
            && Guid.TryParse(record.ActiveSchemeGuidBefore, out var scheme))
        {
            try
            {
                uint rc = PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                if (rc != 0) errors.Add($"PowerSetActiveScheme: код {rc}");
            }
            catch (Exception ex) { errors.Add($"Активная схема: {ex.Message}"); }
        }

        // Небольшая задержка чтобы изменения применились
        await Task.Delay(200, cancellationToken);

        if (errors.Count == 0)
            return OperationResult.Ok($"Восстановлено: реестр {restoredRegistry}, питание {restoredPower}.");

        return OperationResult.Fail(
            $"Восстановлено частично: реестр {restoredRegistry}, питание {restoredPower}, ошибок {errors.Count}.",
            detail: string.Join("; ", errors));
    }

    /// <summary>
    /// Восстановить одно значение реестра: если WasMissing — удалить, иначе записать с тем же kind.
    /// </summary>
    private static void RestoreRegistryEntry(RegistryBackupEntry entry)
    {
        using var key = entry.RootHive?.OpenSubKey(entry.KeyPath, writable: true);
        if (key == null)
        {
            // Ключа нет — возможно, был удалён. Если значение было — пересоздавать не будем.
            return;
        }

        if (entry.WasMissing)
        {
            if (key.GetValue(entry.ValueName) != null)
                key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
            return;
        }

        var value = DeserializeRegistryValue(entry.SerializedValue, entry.ValueKind);
        key.SetValue(entry.ValueName, value, (RegistryValueKind)entry.ValueKind);
    }

    /// <summary>
    /// Восстановить одно значение power-plan через PowerWriteAC/DCValueIndex + PowerApplySetting.
    /// </summary>
    private static void RestorePowerEntry(PowerBackupEntry entry)
    {
        if (!Guid.TryParse(entry.SchemeGuid, out var scheme)) return;
        if (!Guid.TryParse(entry.SubGroupGuid, out var sub)) return;
        if (!Guid.TryParse(entry.SettingGuid, out var setting)) return;

        if (entry.IsAc)
            PowrProf.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, entry.OriginalValue);
        else
            PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, entry.OriginalValue);

        // Немедленное применение через повторную активацию схемы
        PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
    }

    /// <summary>Десериализовать сохранённое значение реестра в объект нужного типа.</summary>
    private static object DeserializeRegistryValue(string? serialized, int kind) => (RegistryValueKind)kind switch
    {
        RegistryValueKind.DWord => int.Parse(serialized ?? "0"),
        RegistryValueKind.QWord => long.Parse(serialized ?? "0"),
        RegistryValueKind.String => serialized ?? string.Empty,
        RegistryValueKind.ExpandString => serialized ?? string.Empty,
        RegistryValueKind.MultiString => (serialized ?? string.Empty).Split('\0'),
        RegistryValueKind.Binary => serialized is null ? Array.Empty<byte>() : ConvertHexStringToBytes(serialized),
        _ => serialized ?? string.Empty
    };

    private static byte[] ConvertHexStringToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    /// <summary>Удалить старые бэкапы сверх лимита (FIFO по дате создания).</summary>
    private void PruneOldBackups()
    {
        try
        {
            int keep = 20;
            try
            {
                // Soft-read settings without circular DI
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var json = File.ReadAllText(AppPaths.SettingsFile);
                    var m = System.Text.RegularExpressions.Regex.Match(json, "\"MaxBackupsToKeep\"\\s*:\\s*(\\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0)
                        keep = Math.Clamp(n, 1, 200);
                }
            }
            catch { /* keep default */ }

            var files = Directory.GetFiles(BackupDirectory, "backup_*.json")
                .OrderByDescending(File.GetCreationTimeUtc)
                .Skip(keep)
                .ToList();
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }
}
