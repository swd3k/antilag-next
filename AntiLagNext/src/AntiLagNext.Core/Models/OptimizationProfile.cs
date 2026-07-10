using AntiLagNext.Core.Enums;

namespace AntiLagNext.Core.Models;

/// <summary>
/// Профиль оптимизации — именованный набор включённых оптимизаций и их параметров.
/// Сериализуется в JSON и хранится в настройках приложения.
/// </summary>
public sealed class OptimizationProfile
{
    /// <summary>Уникальный идентификатор профиля (для ссылок из Game Detection).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Отображаемое имя профиля.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Тип профиля (предустановленный или пользовательский).</summary>
    public ProfileKind Kind { get; set; } = ProfileKind.Custom;

    /// <summary>Описание для пользователя (показывается в UI).</summary>
    public string Description { get; set; } = string.Empty;

    // --- Флаги включения отдельных оптимизаций ---

    /// <summary>Удерживать высокое разрешение таймера.</summary>
    public bool EnableTimer { get; set; }

    /// <summary>Желаемое разрешение таймера в миллисекундах (0.5, 1.0 и т.д.).</summary>
    public double TimerTargetMs { get; set; } = 0.5;

    /// <summary>Переключать схему электропитания.</summary>
    public bool EnablePowerScheme { get; set; }

    /// <summary>true — Ultimate Performance (e9a42b02-…); false — High Performance (8c5e7fda-…).</summary>
    public bool UseUltimatePerformance { get; set; }

    /// <summary>Управлять парковкой ядер.</summary>
    public bool EnableCoreParkingControl { get; set; }

    /// <summary>Режим парковки.</summary>
    public CoreParkingMode CoreParkingMode { get; set; } = CoreParkingMode.AllActive;

    /// <summary>Твик Game Mode / Game Bar / Game DVR.</summary>
    public bool EnableGameModeTweak { get; set; }

    /// <summary>Включить HAGS (Hardware-accelerated GPU scheduling).</summary>
    public bool EnableHags { get; set; }

    /// <summary>Периодически очищать рабочий набор памяти фоновых процессов.</summary>
    public bool EnableMemoryCleanup { get; set; }

    /// <summary>Список процессов, исключаемых из очистки памяти (имя без пути, без регистра).</summary>
    public List<string> MemoryCleanupExclusions { get; set; } = new()
    {
        "AntiLagNext", "explorer", "csrss", "dwm", "winlogon", "lsass", "services",
        "System", "Idle", "MsMpEng", "SecurityHealthService"
    };

    /// <summary>GPU Low Latency Mode (NVIDIA / AMD).</summary>
    public bool EnableGpuLowLatency { get; set; }

    /// <summary>Максимум pre-rendered frames (1 = мин. задержка, 0 = не трогать).</summary>
    public int MaxPreRenderedFrames { get; set; } = 1;

    /// <summary>Список исполняемых файлов игр для авто-активации профиля (без пути, с расширением).</summary>
    public List<string> GameExecutables { get; set; } = new();

    /// <summary>
    /// Создаёт предустановленный профиль заданного типа с разумными параметрами.
    /// </summary>
    public static OptimizationProfile CreatePreset(ProfileKind kind) => kind switch
    {
        ProfileKind.Gaming => new OptimizationProfile
        {
            Name = "Игровой",
            Kind = ProfileKind.Gaming,
            Description = "Максимальная отзывчивость: таймер 0.5 мс, High Performance, все ядра активны, Game Mode/HAGS. Выше энергопотребление и температура.",
            EnableTimer = true,
            TimerTargetMs = 0.5,
            EnablePowerScheme = true,
            UseUltimatePerformance = false,
            EnableCoreParkingControl = true,
            CoreParkingMode = CoreParkingMode.AllActive,
            EnableGameModeTweak = true,
            EnableHags = true,
            EnableMemoryCleanup = true,
            EnableGpuLowLatency = true,
            MaxPreRenderedFrames = 1,
        },
        ProfileKind.Office => new OptimizationProfile
        {
            Name = "Офисный",
            Kind = ProfileKind.Office,
            Description = "Мягкие настройки для повседневной работы: таймер 1.0 мс, High Performance, P-cores активны, E-cores в покое. Тише и прохладнее.",
            EnableTimer = true,
            TimerTargetMs = 1.0,
            EnablePowerScheme = true,
            UseUltimatePerformance = false,
            EnableCoreParkingControl = true,
            CoreParkingMode = CoreParkingMode.KeepEfficientIdle,
            EnableGameModeTweak = false,
            EnableHags = false,
            EnableMemoryCleanup = false,
            EnableGpuLowLatency = false,
            MaxPreRenderedFrames = 0,
        },
        ProfileKind.MaxPerformance => new OptimizationProfile
        {
            Name = "Максимальная производительность",
            Kind = ProfileKind.MaxPerformance,
            Description = "Ultimate Performance (если доступна), таймер 0.5 мс, все ядра, Game Mode/HAGS/GPU LLM. Максимум тепла и потребления.",
            EnableTimer = true,
            TimerTargetMs = 0.5,
            EnablePowerScheme = true,
            UseUltimatePerformance = true,
            EnableCoreParkingControl = true,
            CoreParkingMode = CoreParkingMode.AllActive,
            EnableGameModeTweak = true,
            EnableHags = true,
            EnableMemoryCleanup = true,
            EnableGpuLowLatency = true,
            MaxPreRenderedFrames = 1,
        },
        _ => new OptimizationProfile
        {
            Name = "По умолчанию",
            Kind = ProfileKind.Default,
            Description = "Система в исходном состоянии: все оптимизации отключены.",
        }
    };
}
