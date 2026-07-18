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
        object? value = null;
        var kind = RegistryValueKind.Unknown;
        bool missing = true;

        try
        {
            using var key = root.OpenSubKey(keyPath, writable: false);
            if (key != null)
            {
                // GetValueKind throws if the name does not exist — never call it blindly.
                value = key.GetValue(valueName, null);
                if (value != null)
                {
                    missing = false;
                    try { kind = key.GetValueKind(valueName); }
                    catch { kind = InferKind(value); }
                }
            }
        }
        catch
        {
            // Treat as missing — apply path will create the key/value.
            value = null;
            missing = true;
            kind = RegistryValueKind.Unknown;
        }

        var entry = new RegistryBackupEntry
        {
            Hive = HiveToString(root),
            KeyPath = keyPath,
            ValueName = valueName,
            ValueKind = (int)(missing ? RegistryValueKind.Unknown : kind),
            SerializedValue = SerializeRegistryValue(value, kind),
            WasMissing = missing
        };
        SnapshotRegistryValue(sessionId, entry);
    }

    private static RegistryValueKind InferKind(object value) => value switch
    {
        int => RegistryValueKind.DWord,
        long => RegistryValueKind.QWord,
        string => RegistryValueKind.String,
        string[] => RegistryValueKind.MultiString,
        byte[] => RegistryValueKind.Binary,
        _ => RegistryValueKind.Unknown
    };

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

    public void SnapshotService(Guid sessionId, ServiceBackupEntry entry)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var record))
                record.ServiceEntries.Add(entry);
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

            // Size cap: reject absurd / malicious huge JSON
            var fi = new FileInfo(latest);
            if (fi.Length > 2 * 1024 * 1024)
                return OperationResult<BackupRecord>.Fail("Бэкап слишком большой (лимит 2 МБ).", detail: latest);

            var record = JsonStorage.Load<BackupRecord>(latest);
            if (record == null)
                return OperationResult<BackupRecord>.Fail("Бэкап повреждён.", detail: latest);

            // Cap entry counts against DoS
            if (record.RegistryEntries.Count > 500 || record.PowerEntries.Count > 500 || record.ServiceEntries.Count > 200)
                return OperationResult<BackupRecord>.Fail("Бэкап содержит слишком много записей.");

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
            if (record?.SourceFilePath is not { Length: > 0 } path)
                return OperationResult.Fail("Файл бэкапа не найден.");

            // Path traversal guard: only files under BackupDirectory
            if (!IsPathInsideDirectory(path, BackupDirectory))
                return OperationResult.Fail("Отказ: путь бэкапа вне каталога приложения.");

            if (!File.Exists(path))
                return OperationResult.Fail("Файл бэкапа не найден.");

            File.Delete(path);
            return OperationResult.Ok("Бэкап удалён.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Не удалось удалить бэкап.", detail: ex.Message, ex: ex);
        }
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string fullDir = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<OperationResult> RestoreAsync(BackupRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) return OperationResult.Fail("Пустая запись бэкапа.");

        int restoredRegistry = 0, restoredPower = 0, restoredServices = 0;
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

        // --- Восстановление служб ---
        foreach (var entry in record.ServiceEntries)
        {
            try
            {
                RestoreServiceEntry(entry);
                restoredServices++;
            }
            catch (Exception ex)
            {
                errors.Add($"Service {entry.ServiceName}: {ex.Message}");
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
            return OperationResult.Ok(
                $"Восстановлено: реестр {restoredRegistry}, питание {restoredPower}, службы {restoredServices}.");

        return OperationResult.Fail(
            $"Восстановлено частично: реестр {restoredRegistry}, питание {restoredPower}, службы {restoredServices}, ошибок {errors.Count}.",
            detail: string.Join("; ", errors));
    }

    private static void RestoreServiceEntry(ServiceBackupEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ServiceName) || entry.ServiceName.Length > 256)
            throw new InvalidOperationException("Некорректное имя службы.");
        if (entry.ServiceName.Contains("..", StringComparison.Ordinal)
            || entry.ServiceName.Contains('\\')
            || entry.ServiceName.Contains('/'))
            throw new InvalidOperationException("Небезопасное имя службы.");

        // Restore Start type via registry (safe for allowlisted names only)
        if (!ServiceAllowList.IsSafe(entry.ServiceName))
            throw new InvalidOperationException("Служба не в allowlist восстановления.");

        if (!RegistryPathPolicy.IsValidServiceStartType(entry.OriginalStartType))
            throw new InvalidOperationException("Некорректный Start type службы (ожидается 0–4).");

        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Services\{entry.ServiceName}", writable: true)
            ?? throw new InvalidOperationException("Ключ службы не найден.");

        key.SetValue("Start", entry.OriginalStartType, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Восстановить одно значение реестра: если WasMissing — удалить, иначе записать с тем же kind.
    /// </summary>
    private static void RestoreRegistryEntry(RegistryBackupEntry entry)
    {
        if (!RegistryPathPolicy.IsSafeRegistryPath(entry.Hive, entry.KeyPath, entry.ValueName))
            throw new InvalidOperationException("Небезопасный путь реестра в бэкапе.");

        var root = entry.RootHive
            ?? throw new InvalidOperationException("Неизвестный hive: " + entry.Hive);

        using var key = root.OpenSubKey(entry.KeyPath, writable: true);
        if (key == null)
        {
            // Не создаём произвольные ключи из чужого JSON — только уже существующие.
            return;
        }

        if (entry.WasMissing)
        {
            if (key.GetValue(entry.ValueName) != null)
                key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
            return;
        }

        var value = DeserializeRegistryValue(entry.SerializedValue, entry.ValueKind);
        var kind = (RegistryValueKind)entry.ValueKind;
        if (!Enum.IsDefined(typeof(RegistryValueKind), kind) || kind == RegistryValueKind.Unknown)
            throw new InvalidOperationException("Некорректный ValueKind в бэкапе.");

        key.SetValue(entry.ValueName, value, kind);
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
        RegistryValueKind.DWord => int.TryParse(serialized, out int d) ? d : 0,
        RegistryValueKind.QWord => long.TryParse(serialized, out long q) ? q : 0L,
        RegistryValueKind.String => Truncate(serialized ?? string.Empty, 4096),
        RegistryValueKind.ExpandString => Truncate(serialized ?? string.Empty, 4096),
        RegistryValueKind.MultiString => (serialized ?? string.Empty).Split('\0', StringSplitOptions.None).Take(64).ToArray(),
        RegistryValueKind.Binary => serialized is null ? Array.Empty<byte>() : ConvertHexStringToBytes(serialized),
        _ => Truncate(serialized ?? string.Empty, 4096)
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static byte[] ConvertHexStringToBytes(string hex)
    {
        if (hex.Length == 0 || hex.Length % 2 != 0 || hex.Length > 8192)
            return Array.Empty<byte>();
        for (int i = 0; i < hex.Length; i++)
        {
            char c = hex[i];
            bool ok = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!ok) return Array.Empty<byte>();
        }
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
