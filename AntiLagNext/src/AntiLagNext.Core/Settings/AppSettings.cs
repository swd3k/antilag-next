using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Core.Settings;

/// <summary>
/// Пользовательские настройки приложения (сериализуемые в %APPDATA%\AntiLagNext\settings.json).
/// Хранят текущий профиль, тему, параметры мониторинга и список бэкапов.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Версия схемы настроек (для будущих миграций).</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>true, если первый запуск уже пройден (бенчмарк выполнен).</summary>
    public bool FirstRunCompleted { get; set; }

    /// <summary>Текущая тема UI.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Идентификатор активного профиля (null = профиль по умолчанию).</summary>
    public Guid? ActiveProfileId { get; set; }

    /// <summary>Сохранённые профили (предустановленные + пользовательские).</summary>
    public List<OptimizationProfile> Profiles { get; set; } = new();

    /// <summary>true, если мониторинг задержек включён и работает в фоне.</summary>
    public bool MonitoringEnabled { get; set; } = true;

    /// <summary>Интервал обновления мониторинга, мс.</summary>
    public int MonitoringIntervalMs { get; set; } = 500;

    /// <summary>true, если авто-переключение профиля по запускам игр включено.</summary>
    public bool GameAutoSwitchEnabled { get; set; } = true;

    /// <summary>Создавать ли точку восстановления перед изменениями (рекомендуется).</summary>
    public bool CreateRestorePoint { get; set; } = true;

    /// <summary>Максимальное число хранимых бэкапов (FIFO).</summary>
    public int MaxBackupsToKeep { get; set; } = 20;

    /// <summary>Сворачивать в трей вместо закрытия (держать процесс и timer resolution).</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>При полном выходе отпускать таймер (false — оставить resolution до kill процесса).</summary>
    public bool ReleaseTimerOnExit { get; set; } = true;

    /// <summary>
    /// Создаёт настройки с предустановленными профилями по умолчанию.
    /// </summary>
    public static AppSettings CreateDefault()
    {
        var profiles = new List<OptimizationProfile>
        {
            OptimizationProfile.CreatePreset(ProfileKind.Default),
            OptimizationProfile.CreatePreset(ProfileKind.Gaming),
            OptimizationProfile.CreatePreset(ProfileKind.Office)
        };

        return new AppSettings
        {
            Profiles = profiles,
            ActiveProfileId = profiles[0].Id
        };
    }

    /// <summary>
    /// Находит активный профиль. Если не найден — возвращает профиль по умолчанию.
    /// </summary>
    public OptimizationProfile GetActiveProfile()
    {
        if (ActiveProfileId is { } id)
        {
            var found = Profiles.FirstOrDefault(p => p.Id == id);
            if (found != null) return found;
        }
        return Profiles.FirstOrDefault(p => p.Kind == ProfileKind.Default)
               ?? OptimizationProfile.CreatePreset(ProfileKind.Default);
    }
}
