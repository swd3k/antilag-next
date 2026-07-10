using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Core.Settings;

/// <summary>
/// User settings (serialized to %APPDATA%\AntiLagNext\settings\user-settings.json).
/// Profiles, theme, monitoring, backups.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Latest settings schema version (migrations bump this).</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Settings schema version (for migrations).</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

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
    /// Finds the active profile. Falls back to Default when id is missing/unknown.
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

    /// <summary>
    /// Ensures built-in presets exist, normalizes legacy Russian display names to English,
    /// and bumps <see cref="SchemaVersion"/>. Returns true if settings should be re-saved.
    /// </summary>
    public bool MigrateToCurrentSchema()
    {
        bool dirty = false;

        dirty |= EnsureBuiltInPresets();
        dirty |= NormalizeBuiltInProfileLabels();

        if (SchemaVersion < CurrentSchemaVersion)
        {
            SchemaVersion = CurrentSchemaVersion;
            dirty = true;
        }
        else if (SchemaVersion > CurrentSchemaVersion)
        {
            // Future client wrote a higher version — clamp, still try to run safely
            SchemaVersion = CurrentSchemaVersion;
            dirty = true;
        }

        return dirty;
    }

    /// <summary>Insert missing Default / Gaming / Office / MaxPerformance presets.</summary>
    public bool EnsureBuiltInPresets()
    {
        bool dirty = false;
        void Ensure(ProfileKind kind)
        {
            if (Profiles.Any(p => p.Kind == kind)) return;
            if (kind == ProfileKind.Default)
                Profiles.Insert(0, OptimizationProfile.CreatePreset(kind));
            else
                Profiles.Add(OptimizationProfile.CreatePreset(kind));
            dirty = true;
        }

        Ensure(ProfileKind.Default);
        Ensure(ProfileKind.Gaming);
        Ensure(ProfileKind.Office);
        Ensure(ProfileKind.MaxPerformance);
        return dirty;
    }

    /// <summary>
    /// Built-in profiles store English stable names; UI localizes via Kind / UiId.
    /// Migrates legacy RU names (Игровой, Офисный, …).
    /// </summary>
    public bool NormalizeBuiltInProfileLabels()
    {
        bool dirty = false;
        foreach (var p in Profiles)
        {
            if (p.Kind == ProfileKind.Custom) continue;

            string expectedName = OptimizationProfile.DefaultEnglishName(p.Kind);
            if (!string.Equals(p.Name, expectedName, StringComparison.Ordinal))
            {
                p.Name = expectedName;
                dirty = true;
            }

            // Keep description in English for built-ins (UI does not show these raw in Photino)
            var fresh = OptimizationProfile.CreatePreset(p.Kind);
            if (string.IsNullOrWhiteSpace(p.Description)
                || LooksLegacyRussian(p.Description)
                || p.Description.Contains("Максимальная отзывчивость", StringComparison.Ordinal)
                || p.Description.Contains("Мягкие настройки", StringComparison.Ordinal)
                || p.Description.Contains("Система в исходном", StringComparison.Ordinal))
            {
                if (!string.Equals(p.Description, fresh.Description, StringComparison.Ordinal))
                {
                    p.Description = fresh.Description;
                    dirty = true;
                }
            }
        }

        return dirty;
    }

    private static bool LooksLegacyRussian(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            if (c is >= 'А' and <= 'я' or 'Ё' or 'ё')
                return true;
        }
        return false;
    }
}
