using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AntiLagNext.Core.Models;

/// <summary>
/// Запись об одном значении реестра, сохранённом перед изменением.
/// Хранится в JSON-файле бэкапа. Восстанавливается с тем же <see cref="ValueKind"/>.
/// </summary>
public sealed class RegistryBackupEntry
{
    /// <summary>Корневой куст в виде строки ("HKLM", "HKCU", "HKCR", "HKU", "HKCC").</summary>
    public string Hive { get; set; } = "HKLM";

    /// <summary>Путь ключа (без корневого куста).</summary>
    public string KeyPath { get; set; } = string.Empty;

    /// <summary>Имя значения. null или пусто — значение "(по умолчанию)".</summary>
    public string ValueName { get; set; } = string.Empty;

    /// <summary>Тип значения реестра (как число из перечисления Win32).</summary>
    public int ValueKind { get; set; }

    /// <summary>
    /// Исходное значение, сериализованное в строку.
    /// Для REG_DWORD/REG_QWORD — десятичное число в строке; для REG_SZ/REG_EXPAND_SZ — текст;
    /// для REG_MULTI_SZ — строки через \0; для REG_BINARY — hex.
    /// </summary>
    public string? SerializedValue { get; set; }

    /// <summary>Признак того, что значение отсутствовало до изменения (его нужно удалить при откате).</summary>
    public bool WasMissing { get; set; }

    /// <summary>
    /// Преобразует строковый куст в <see cref="RegistryKey"/>.
    /// </summary>
    [JsonIgnore]
    public RegistryKey? RootHive => Hive switch
    {
        "HKLM" => Registry.LocalMachine,
        "HKCU" => Registry.CurrentUser,
        "HKCR" => Registry.ClassesRoot,
        "HKU" => Registry.Users,
        "HKCC" => Registry.CurrentConfig,
        _ => null
    };
}

/// <summary>
/// Запись об одном значении power-plan, сохранённом перед изменением (через PowerReadAC/DCValueIndex).
/// </summary>
public sealed class PowerBackupEntry
{
    /// <summary>GUID схемы электропитания.</summary>
    public string SchemeGuid { get; set; } = string.Empty;

    /// <summary>GUID подгруппы настроек.</summary>
    public string SubGroupGuid { get; set; } = string.Empty;

    /// <summary>GUID конкретной настройки.</summary>
    public string SettingGuid { get; set; } = string.Empty;

    /// <summary>true — AC (от сети), false — DC (от батареи).</summary>
    public bool IsAc { get; set; }

    /// <summary>Исходное значение настройки.</summary>
    public uint OriginalValue { get; set; }

    /// <summary>Признак того, что настройка была скрыта/отсутствовала (требует unhide перед восстановлением).</summary>
    public bool WasHidden { get; set; }
}

/// <summary>
/// Полная запись бэкапа для одной операции «Перед изменениями».
/// Содержит снимок всех изменённых значений реестра и power-plan, а также активную схему до изменений.
/// </summary>
public sealed class BackupRecord
{
    /// <summary>UTC-время создания бэкапа (ISO-8601).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Понятное описание операции (например, "Активация профиля 'Игровой'").</summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>Активная схема электропитания ДО изменений (GUID строки).</summary>
    public string? ActiveSchemeGuidBefore { get; set; }

    /// <summary>Изменённые значения реестра.</summary>
    public List<RegistryBackupEntry> RegistryEntries { get; set; } = new();

    /// <summary>Изменённые значения power-plan.</summary>
    public List<PowerBackupEntry> PowerEntries { get; set; } = new();

    /// <summary>Создана ли точка восстановления системы (System Restore Point).</summary>
    public bool SystemRestorePointCreated { get; set; }

    /// <summary>Описание ошибки создания точки восстановления (если не создана).</summary>
    public string? SystemRestorePointError { get; set; }

    /// <summary>Путь к JSON-файлу на диске (заполняется при LoadAll, не сериализуется).</summary>
    [JsonIgnore]
    public string? SourceFilePath { get; set; }

    /// <summary>Краткое описание для UI-списка.</summary>
    [JsonIgnore]
    public string DisplaySummary =>
        $"{CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {OperationName} · " +
        $"рег:{RegistryEntries.Count} power:{PowerEntries.Count}" +
        (SystemRestorePointCreated ? " · RP✓" : "");
}
