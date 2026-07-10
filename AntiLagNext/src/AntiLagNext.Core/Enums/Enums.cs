namespace AntiLagNext.Core.Enums;

/// <summary>
/// Режим управления парковкой ядер для гетерогенных процессоров (P-cores/E-cores).
/// На обычных однородных CPU оба режима ведут себя одинаково.
/// </summary>
public enum CoreParkingMode
{
    /// <summary>
    /// Все ядра активны (парковка отключена). Максимальная отзывчивость, выше энергопотребление.
    /// </summary>
    AllActive = 0,

    /// <summary>
    /// Не трогать E-cores (энергоэффективные ядра): они остаются парковаться, активны только P-cores.
    /// Баланс отзывчивости и энергопотребления для Intel 12+ поколений.
    /// </summary>
    KeepEfficientIdle = 1,

    /// <summary>
    /// Не изменять настройку парковки (системное значение по умолчанию).
    /// </summary>
    SystemDefault = 2
}

/// <summary>
/// Тип оптимизации — используется для гранулярного включения/отключения и для журнала бэкапа.
/// </summary>
public enum OptimizationKind
{
    Timer = 0,
    PowerScheme = 1,
    CoreParking = 2,
    GameMode = 3,
    Memory = 4,
    Hags = 5,
    GpuLowLatency = 6
}

/// <summary>
/// Предустановленный профиль оптимизации. Пользовательские профили имеют тип <c>Custom</c>
/// и хранят произвольный набор включённых оптимизаций.
/// </summary>
public enum ProfileKind
{
    /// <summary>Профиль по умолчанию — все оптимизации выключены, система в исходном состоянии.</summary>
    Default = 0,

    /// <summary>Игровой профиль — таймер 0.5 мс, High Performance, парковка отключена, Game Mode/HAGS.</summary>
    Gaming = 1,

    /// <summary>Офисный профиль — мягкие настройки, баланс отзывчивости и тишины/энергии.</summary>
    Office = 2,

    /// <summary>Пользовательский набор.</summary>
    Custom = 3,

    /// <summary>Максимальная производительность — Ultimate Performance, таймер 0.5 мс, все твики.</summary>
    MaxPerformance = 4
}

/// <summary>
/// Тема оформления UI.
/// </summary>
public enum AppTheme
{
    Dark = 0,
    Light = 1,
    /// <summary>Следовать системной теме Windows.</summary>
    System = 2
}

/// <summary>
/// Тип источника питания компьютера (для раздельных настроек AC/DC).
/// </summary>
public enum PowerSource
{
    /// <summary>От сети (AC).</summary>
    Ac = 0,

    /// <summary>От батареи (DC) — для ноутбуков.</summary>
    Dc = 1
}
