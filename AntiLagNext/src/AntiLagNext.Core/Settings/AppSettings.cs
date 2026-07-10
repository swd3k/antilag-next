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

    /// <summary>Текущая тема UI (Dark / Light / System).</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>UI culture: ru, en, … (JSON packs in i18n/).</summary>
    public string UiCulture { get; set; } = "ru";

    /// <summary>Plugin id → enabled. Core facades always documented as on.</summary>
    public Dictionary<string, bool> PluginEnabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Идентификатор активного профиля (null = профиль по умолчанию).</summary>
    public Guid? ActiveProfileId { get; set; }

    /// <summary>Сохранённые профили (предустановленные + пользовательские).</summary>
    public List<OptimizationProfile> Profiles { get; set; } = new();

    /// <summary>
    /// Live latency chart / high-frequency probe (default 5 ms). When false — probe thread stopped.
    /// </summary>
    public bool MonitoringEnabled { get; set; } = true;

    /// <summary>Интервал обновления графика, мс (UI chart: 15).</summary>
    public int MonitoringIntervalMs { get; set; } = 15;

    /// <summary>true, если авто-переключение профиля по запускам игр включено.</summary>
    public bool GameAutoSwitchEnabled { get; set; } = true;

    /// <summary>Создавать ли точку восстановления перед изменениями (рекомендуется).</summary>
    public bool CreateRestorePoint { get; set; } = true;

    /// <summary>Максимальное число хранимых бэкапов (FIFO).</summary>
    public int MaxBackupsToKeep { get; set; } = 20;

    /// <summary>
    /// Сворачивать в трей вместо закрытия.
    /// Default false until user enables optimization once (then forced on).
    /// </summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>Запуск вместе с Windows (Task Scheduler ONLOGON, highest privileges).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// При старте сразу apply (только после того, как пользователь сам включил оптимизацию).
    /// Default false — первый запуск без авто-оптимизации.
    /// </summary>
    public bool AutoApplyOnStartup { get; set; }

    /// <summary>
    /// Пользователь хотя бы раз успешно включил оптимизацию (кнопка Enable).
    /// </summary>
    public bool UserEnabledOptimization { get; set; }

    /// <summary>При полном выходе отпускать таймер (false — оставить resolution до kill процесса).</summary>
    public bool ReleaseTimerOnExit { get; set; } = true;

    /// <summary>
    /// Allow loading external plugins from {BaseDir}/plugins/*.dll.
    /// Default false: only built-in plugins; third-party DLLs run elevated and are opt-in.
    /// </summary>
    public bool AllowExternalPlugins { get; set; }

    /// <summary>
    /// Создаёт настройки с предустановленными профилями по умолчанию.
    /// </summary>
    public static AppSettings CreateDefault()
    {
        var profiles = new List<OptimizationProfile>
        {
            OptimizationProfile.CreatePreset(ProfileKind.Default),
            OptimizationProfile.CreatePreset(ProfileKind.Gaming),
            OptimizationProfile.CreatePreset(ProfileKind.Office),
            OptimizationProfile.CreatePreset(ProfileKind.MaxPerformance)
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
